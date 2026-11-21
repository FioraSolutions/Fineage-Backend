using Fineage.Protocol;
using Fineage.Zone.Punishment;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld : IPunishmentSink
{
    PunishmentManager _punishments = null!;
    PunishmentActor _punishmentActor = null!;
    readonly Dictionary<int, long> _jailReturnDueMs = [];

    public PunishmentManager PunishmentManager => _punishments;

    internal long PunishmentNowUnixMs() => NowUnixMs();

    void InitPunishment(PunishmentManager? manager)
    {
        _punishments = manager ?? new PunishmentManager(
          new MemoryPunishmentStore(),
          JailZoneCatalog.LoadDefault(),
          new GmAuditLog(Path.Combine(Path.GetTempPath(), "fineage-gm-audit-test.jsonl")));
        _punishmentActor = new PunishmentActor(_punishments);
        _punishments.Bind(_accounts, this);
    }

    public void UsePunishments(PunishmentManager manager)
    {
        _punishments = manager ?? throw new ArgumentNullException(nameof(manager));
        _punishmentActor = new PunishmentActor(_punishments);
        _punishments.Bind(_accounts, this);
    }

    void TickPunishments(long nowUnixMs)
    {
        _punishmentActor.Step(nowUnixMs);
        TickJailEscapeGuard(nowUnixMs);
    }

    void IPunishmentSink.OnPunishmentStarted(PunishmentRow row)
    {
        if (row.Affect == PunishmentAffect.Character && int.TryParse(row.Key, out int objectId))
        {
            switch (row.Type)
            {
                case PunishmentType.ChatBan:
                    ApplyChatBanToOnline(objectId, row.ExpirationUnixMs, active: true);
                    break;
                case PunishmentType.Jail:
                    ApplyJailToOnline(objectId, row);
                    break;
                case PunishmentType.Ban:
                    KickBannedOnline(objectId);
                    break;
            }
        }
        else if (row.Affect == PunishmentAffect.Account && row.Type == PunishmentType.Ban)
        {
            foreach (var player in Players())
            {
                if (player.Session != null &&
                    string.Equals(player.Session.Account, row.Key, StringComparison.OrdinalIgnoreCase))
                    KickBannedOnline(player.ObjectId);
            }
        }
    }

    void IPunishmentSink.OnPunishmentEnded(PunishmentRow row)
    {
        if (row.Affect != PunishmentAffect.Character || !int.TryParse(row.Key, out int objectId))
            return;
        switch (row.Type)
        {
            case PunishmentType.ChatBan:
                ApplyChatBanToOnline(objectId, 0, active: false);
                break;
            case PunishmentType.Jail:
                ReleaseJailOnline(objectId);
                _jailReturnDueMs.Remove(objectId);
                break;
        }
    }

    bool PunishmentBlocksEnter(ZoneSession session, long nowUnixMs)
    {
        if (_punishments.IsBanned(session.Account, session.ObjectId, nowUnixMs))
        {
            ZoneLog.Warn($"enter refused object={session.ObjectId} account={session.Account} reason=ban");
            return true;
        }

        return false;
    }

    void SyncPunishmentsOnEnter(ZoneSession session, ZoneEntity player, long nowUnixMs)
    {
        session.ChatBannedUntilUnixMs = SessionChatBanUntil(_punishments.ChatBanUntilUnixMs(session.ObjectId, nowUnixMs));
        if (_punishments.IsJailed(session.ObjectId, nowUnixMs))
        {
            var jail = _punishments.Jail.LocationIn;
            if (!_punishments.Jail.Contains(player.X, player.Y, player.Z))
                Relocate(player, jail.X, jail.Y, jail.Z);
        }
    }

    static long SessionChatBanUntil(long chatBanUntilUnixMs) =>
      chatBanUntilUnixMs switch
      {
          < 0 => 0,
          0 => long.MaxValue,
          _ => chatBanUntilUnixMs,
      };

    void ApplyChatBanToOnline(int objectId, long expirationUnixMs, bool active)
    {
        if (!_byId.TryGetValue(objectId, out var player) || player.Session == null)
            return;
        player.Session.ChatBannedUntilUnixMs = active ? SessionChatBanUntil(expirationUnixMs) : 0;
        NetBridge?.FanoutSystemMessage(
          [objectId],
          new SystemMessageMsg
          {
              Id = active ? ChatProtocol.ChattingProhibited : ChatProtocol.SystemPlain,
              Text = active ? "Chatting is currently prohibited." : "Chat has been enabled.",
              Params = []
          });
    }

    void ApplyJailToOnline(int objectId, PunishmentRow row)
    {
        if (!_byId.TryGetValue(objectId, out var player) || player.Session == null)
            return;
        var jail = _punishments.Jail.LocationIn;
        Relocate(player, jail.X, jail.Y, jail.Z);
        _jailReturnDueMs.Remove(objectId);
        NetBridge?.FanoutSystemMessage(
          [objectId],
          new SystemMessageMsg
          {
              Id = ChatProtocol.SystemPlain,
              Text = "You have been jailed. " + row.Reason,
              Params = []
          });
        ZoneLog.Info($"jail applied object={objectId} by={row.PunishedBy}");
    }

    void ReleaseJailOnline(int objectId)
    {
        if (!_byId.TryGetValue(objectId, out var player) || player.Session == null)
            return;
        var exit = _punishments.Jail.LocationOut;
        Relocate(player, exit.X, exit.Y, exit.Z);
        NetBridge?.FanoutSystemMessage(
          [objectId],
          new SystemMessageMsg
          {
              Id = ChatProtocol.SystemPlain,
              Text = "You are free for now; respect server rules.",
              Params = []
          });
    }

    void KickBannedOnline(int objectId)
    {
        if (!_byId.TryGetValue(objectId, out var player) || player.Session is not { } session)
            return;
        ZoneLog.Info($"ban kick object={objectId}");
        TryDetachPlayer(session, capturePersist: true, out _, out var snapshot);
        if (snapshot.HasValue)
            PersistUnlinkSnapshot(snapshot.Value);
        session.InWorld = false;
        session.ChatBannedUntilUnixMs = 0;
        _chat.Left(objectId);
    }

    void TickJailEscapeGuard(long nowUnixMs)
    {
        foreach (var player in Players())
        {
            if (player.Session == null || ZoneAdmin.IsAdminCharacter(player.Name))
                continue;
            if (!_punishments.IsJailed(player.ObjectId, nowUnixMs))
                continue;
            if (_punishments.Jail.Contains(player.X, player.Y, player.Z))
                continue;
            if (_jailReturnDueMs.TryGetValue(player.ObjectId, out long due) && nowUnixMs < due)
                continue;
            _jailReturnDueMs[player.ObjectId] = nowUnixMs + 2000;
            var jail = _punishments.Jail.LocationIn;
            Relocate(player, jail.X, jail.Y, jail.Z);
            NetBridge?.FanoutSystemMessage(
              [player.ObjectId],
              new SystemMessageMsg
              {
                  Id = ChatProtocol.SystemPlain,
                  Text = "You cannot cheat your way out of here. You must wait until your jail time is over.",
                  Params = []
              });
        }
    }

    internal void SetChatBan(ZoneSession session, long durationMs)
    {
        long now = NowUnixMs();
        string key = session.ObjectId.ToString();
        if (durationMs <= 0)
        {
            var stop = _punishments.StopPunishment(key, PunishmentAffect.Character, PunishmentType.ChatBan, "system", 0);
            if (stop != null)
                ZoneLog.Warn($"SetChatBan lift refused object={session.ObjectId} reason={stop}");
            return;
        }

        long minutes = Math.Max(1, (durationMs + 59_999) / 60_000);
        var err = _punishments.StartPunishment(
          key,
          PunishmentAffect.Character,
          PunishmentType.ChatBan,
          minutes,
          "chat ban",
          "system",
          0,
          now);
        if (err != null)
            ZoneLog.Warn($"SetChatBan refused object={session.ObjectId} reason={err}");
    }
}