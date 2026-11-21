using Fineage.Zone.Augmentation;
using Fineage.Zone.CursedWeapons;
using Fineage.Zone.Parity;
using Fineage.Zone.Skills;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ClientHeavyParityTests
{
    [Fact]
    public void Client_heavy_features_default_off()
    {
        using var _ = EnvScope.Unset(
            ClientHeavyFeatures.AugmentationVariable,
            ClientHeavyFeatures.CursedWeaponsVariable,
            ClientHeavyFeatures.SkillEnchantVariable);
        Assert.False(ClientHeavyFeatures.AugmentationEnabled);
        Assert.False(ClientHeavyFeatures.CursedWeaponsEnabled);
        Assert.False(ClientHeavyFeatures.SkillEnchantEnabled);
        Assert.Equal(AugmentationCatalog.LoadDefault().IsLoaded, AugmentationRules.CanRefine);
        Assert.False(SkillEnchantRules.CanEnchant(1, 2));
    }

    [Fact]
    public void Cursed_weapon_ids_match_olympiad_gates()
    {
        Assert.True(CursedWeaponIds.IsCursedItem(8190));
        Assert.True(CursedWeaponIds.IsCursedItem(8689));
        Assert.False(CursedWeaponIds.IsCursedItem(57));
    }

    [Fact]
    public void Cursed_weapons_state_ignores_mark_when_disabled()
    {
        using var _ = EnvScope.Unset(ClientHeavyFeatures.CursedWeaponsVariable);
        var state = new CursedWeaponsState();
        Assert.False(state.TryMarkOwned(8190, 1));
        Assert.Empty(state.ActivatedItemIds);
    }

    [Fact]
    public void Cursed_weapons_state_tracks_activation_when_enabled()
    {
        using var env = EnvScope.Set(ClientHeavyFeatures.CursedWeaponsVariable, "true");
        var state = new CursedWeaponsState();
        Assert.True(state.TryMarkOwned(8190, 42));
        Assert.True(state.IsActivated(8190));
        Assert.Contains(8190, state.ActivatedItemIds);
        state.ClearForTest();
    }

    [Fact]
    public void Augmentation_variation_id_splits_like_mobius()
    {
        var (a, b) = AugmentationRules.SplitVariationId(0x0001_0002);
        Assert.Equal(2, a);
        Assert.Equal(1, b);
    }

    sealed class EnvScope : IDisposable
    {
        readonly List<(string Key, string? Prior)> _priors = [];

        EnvScope() { }

        public static EnvScope Unset(params string[] keys)
        {
            var scope = new EnvScope();
            foreach (string key in keys)
            {
                scope._priors.Add((key, Environment.GetEnvironmentVariable(key)));
                Environment.SetEnvironmentVariable(key, null);
            }

            return scope;
        }

        public static EnvScope Set(string key, string value)
        {
            var scope = new EnvScope();
            scope._priors.Add((key, Environment.GetEnvironmentVariable(key)));
            Environment.SetEnvironmentVariable(key, value);
            return scope;
        }

        public void Dispose()
        {
            foreach (var (key, prior) in _priors)
                Environment.SetEnvironmentVariable(key, prior);
        }
    }
}