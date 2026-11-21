using Fineage.Game;
using Fineage.Game.Services;
using Fineage.Zone.Geo;

namespace Fineage.Zone.World;

/// <summary>Mobius SwampZone, DamageZone, and NoLandingZone rules from imported polygons.</summary>
public sealed partial class ZoneWorld
{
    static readonly Lazy<SwampZones> SharedSwamp = new(SwampZones.LoadDefault);
    static readonly Lazy<DamageZones> SharedDamage = new(DamageZones.LoadDefault);
    static readonly Lazy<NoLandingZones> SharedNoLanding = new(NoLandingZones.LoadDefault);
    static readonly Lazy<MotherTreeZones> SharedMotherTree = new(MotherTreeZones.LoadDefault);

    internal SwampZones Swamp { get; set; } = SwampZones.Empty;
    internal DamageZones Damage { get; set; } = DamageZones.Empty;
    internal NoLandingZones NoLanding { get; set; } = NoLandingZones.Empty;
    internal MotherTreeZones MotherTree { get; set; } = MotherTreeZones.Empty;

    void UpdateMoveEnvironment(ZoneEntity e)
    {
        UpdateWaterState(e);
        UpdateSwampState(e);
        UpdateNoLandingState(e);
        UpdateDamageZoneMembership(e);
    }

    void UpdateSwampState(ZoneEntity e)
    {
        if (e.Kind != ZoneEntityKind.Player || Swamp.Count == 0)
            return;
        var zone = Swamp.At(e.X, e.Y, e.Z);
        bool inSwamp = zone != null;
        double bonus = zone?.MoveBonus ?? 1d;
        if (inSwamp == e.InSwamp && Math.Abs(e.SwampMoveBonus - bonus) < 1e-6)
            return;
        e.InSwamp = inSwamp;
        e.SwampMoveBonus = inSwamp ? bonus : 1d;
        RefreshRunSpeed(e);
        ZoneLog.Info($"[swamp] obj={e.ObjectId} {(inSwamp ? "enter" : "exit")} bonus={bonus} speed={MoveSpeedOf(e)}");
        FanoutNetMoveType(e);
    }

    void UpdateNoLandingState(ZoneEntity e)
    {
        if (e.Kind != ZoneEntityKind.Player || NoLanding.Count == 0)
            return;
        var zone = NoLanding.At(e.X, e.Y, e.Z);
        bool inside = zone != null;
        if (!inside)
        {
            if (e.NoLandingDismountAtMs != 0)
                e.NoLandingDismountAtMs = 0;
            return;
        }

        if (e.Mount?.Kind != PetMountKind.Wyvern)
            return;

        int delaySec = zone!.DismountDelaySec;
        long due = NowUnixMs() + delaySec * 1000L;
        if (e.NoLandingDismountAtMs == 0)
        {
            e.NoLandingDismountAtMs = due;
            ZoneLog.Info($"[no-landing] obj={e.ObjectId} wyvern dismount scheduled in {delaySec}s");
        }
    }

    void UpdateDamageZoneMembership(ZoneEntity e)
    {
        if (e.Kind != ZoneEntityKind.Player || Damage.Count == 0)
            return;
        var zone = Damage.At(e.X, e.Y, e.Z);
        if (zone == null)
        {
            e.DamageZoneNextAtMs = 0;
            return;
        }

        if (e.DamageZoneNextAtMs == 0)
            e.DamageZoneNextAtMs = NowUnixMs() + zone.InitialDelayMs;
    }

    void TickEnvironmentZones(long nowUnixMs)
    {
        if (Damage.Count == 0 && NoLanding.Count == 0)
            return;

        foreach (var entity in _byId.Values)
        {
            if (entity.Kind != ZoneEntityKind.Player || entity.Session == null || entity.CurHp <= 0)
                continue;

            if (entity.NoLandingDismountAtMs > 0 && nowUnixMs >= entity.NoLandingDismountAtMs)
            {
                entity.NoLandingDismountAtMs = 0;
                if (entity.Mount?.Kind == PetMountKind.Wyvern)
                    DismountForEnvironment(entity);
            }

            if (entity.DamageZoneNextAtMs == 0 || nowUnixMs < entity.DamageZoneNextAtMs)
                continue;
            var zone = Damage.At(entity.X, entity.Y, entity.Z);
            if (zone == null)
            {
                entity.DamageZoneNextAtMs = 0;
                continue;
            }

            ApplyDamageZoneTick(entity, zone, nowUnixMs);
        }
    }

    void ApplyDamageZoneTick(ZoneEntity entity, DamageZoneDef zone, long nowUnixMs)
    {
        entity.DamageZoneNextAtMs = nowUnixMs + zone.ReuseMs;
        double vuln = 1d + PlayerStats(entity).DamageZoneVuln / 100d;
        bool changed = false;
        if (zone.HpPerTick > 0)
        {
            int dmg = Math.Max(1, (int)Math.Round(zone.HpPerTick * vuln));
            int nextHp = Math.Max(0, entity.CurHp - dmg);
            if (nextHp != entity.CurHp)
            {
                entity.CurHp = nextHp;
                changed = true;
            }
        }

        if (zone.MpPerTick > 0)
        {
            int burn = Math.Max(0, (int)Math.Round(zone.MpPerTick * vuln));
            int nextMp = Math.Max(0, entity.CurMp - burn);
            if (nextMp != entity.CurMp)
            {
                entity.CurMp = nextMp;
                changed = true;
            }
        }

        if (!changed)
            return;
        if (entity.CurHp <= 0)
        {
            KillFromEnvironment(entity);
            return;
        }

        SyncNeedRegen(entity);
        FanoutVitals(entity, StatusFor(entity));
    }

    void KillFromEnvironment(ZoneEntity entity)
    {
        ZoneLog.Info($"[damage] obj={entity.ObjectId} killed in {Damage.At(entity.X, entity.Y, entity.Z)?.Name}");
        ApplyPlayerDeath(entity, killerObjectId: entity.ObjectId);
    }

    internal void DismountForEnvironment(ZoneEntity player) => Dismount(player);
}