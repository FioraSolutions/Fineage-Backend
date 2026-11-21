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
        using var _ = EnvScope.Unset(ClientHeavyFeatures.SkillEnchantVariable);
        Assert.False(ClientHeavyFeatures.SkillEnchantEnabled);
        var augment = AugmentationCatalog.LoadDefault();
        AugmentationRules.BindCatalog(augment);
        Assert.True(augment.IsLoaded, "data/augmentation/*.json must load in test host");
        Assert.True(AugmentationRules.CanRefine);
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
    public void Cursed_weapons_manager_tracks_activation()
    {
        var catalog = CursedWeaponsCatalog.From([
            new CursedWeaponDefinition(8190, "Zariche", 3603, 301, 50, 1, 300, 3, 10)
        ]);
        var manager = new CursedWeaponsManager(catalog);
        Assert.True(manager.TryMarkOwnedForTest(8190, 42, endTimeUnixMs: 9_999_999));
        Assert.Contains(8190, manager.ActiveItemIds());
        manager.ClearForTest();
        Assert.Empty(manager.ActiveItemIds());
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