using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    readonly HerbCatalog _herbs = HerbCatalog.LoadDefault();

    bool IsHerb(int itemId) => _herbs.IsHerb(itemId);

    /// <summary>
    /// Mobius Player.doPickupItem herb branch: ItemSkills.useItem on the picker, then the ground item is destroyed (never enters the bag).
    /// HealPercent / ManaHealPercent restore a percent of max (1066 / 1068); buffs are 300 s abnormals through the shared buff path.
    /// Applied once per picked herb object.
    /// </summary>
    void UseHerbOnPickup(ZoneSession session, ZoneEntity player, ZoneEntity ground, List<Pending> outbound)
    {
        var others = new List<Pending>();
        bool buffed = false, vitals = false;
        foreach (var (skill, level) in _herbs.EffectsOf(ground.GroundItemId))
        {
            switch (skill.Kind)
            {
                case HerbKind.HpPercent:
                {
                    int hp = HerbCatalog.RestorePercent(player.CurHp, player.MaxHp, skill.AmountAt(level));
                    player.CurHp += hp;
                    vitals = true;
                    Notice(session, _herbs.HpRestoredMessage, $"{hp} HP has been restored.", outbound);
                    break;
                }
                case HerbKind.MpPercent:
                {
                    int mp = HerbCatalog.RestorePercent(player.CurMp, player.MaxMp, skill.AmountAt(level));
                    player.CurMp += mp;
                    vitals = true;
                    Notice(session, _herbs.MpRestoredMessage, $"{mp} MP has been restored.", outbound);
                    break;
                }
                default:
                    // Self-cast like ItemSkills: caster = target = picker.
                    CastBuff(player, player, skill.SkillId, level, skill.AbnormalLevel, skill.DurationSeconds, outbound, others);
                    buffed = true;
                    break;
            }
        }

        outbound.AddRange(others);
        if (buffed)
            FanoutAbnormal(player, outbound);
        if (vitals)
        {
            FanoutVitals(player, new StatusUpdateMsg
            {
                ObjectId = player.ObjectId,
                AttributeIds = [InterludeStatus.CurHp, InterludeStatus.MaxHp, InterludeStatus.CurMp, InterludeStatus.MaxMp],
                Values = [player.CurHp, player.MaxHp, player.CurMp, player.MaxMp]
            }, outbound);
        }

        RemoveGroundItem(ground, outbound);
        ZoneLog.Info($"herb object={session.ObjectId} ground={ground.ObjectId} item={ground.GroundItemId} hp={player.CurHp}/{player.MaxHp} mp={player.CurMp}/{player.MaxMp}");
    }

    /// <summary>Test: drop one ground stack next to <paramref name="player"/> and return its object id.</summary>
    internal int PlaceGroundForTest(ZoneEntity player, int itemId, int count = 1)
    {
        lock (_gate)
        {
            var before = _byId.Keys.ToHashSet();
            long now = _clock().ToUnixTimeMilliseconds();
            SpawnGroundStack(itemId, count, player.X, player.Y, player.Z, 0, [player], [],
                expireAtMs: now + (IsHerb(itemId) ? HerbAutoDestroyMs : GroundItemAutoDestroyMs));
            return _byId.Keys.First(id => !before.Contains(id));
        }
    }

    internal bool ExistsForTest(int objectId)
    {
        lock (_gate)
            return _byId.ContainsKey(objectId);
    }
}
