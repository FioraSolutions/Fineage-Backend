using Fineage.Protocol;
using Fineage.Zone.Inventory;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ShotRulesTests
{
    [Fact]
    public void Charge_error_covers_weapon_grade_charge_and_count()
    {
        Assert.Equal(ShotRules.ChargeDeny.CannotUse, ShotRules.ChargeError(0, true, false, 10));
        Assert.Equal(ShotRules.ChargeDeny.GradeMismatch, ShotRules.ChargeError(1, false, false, 10));
        Assert.Equal(ShotRules.ChargeDeny.AlreadyCharged, ShotRules.ChargeError(1, true, true, 10));
        Assert.Equal(ShotRules.ChargeDeny.NotEnough, ShotRules.ChargeError(2, true, false, 1));
        Assert.Equal(ShotRules.ChargeDeny.None, ShotRules.ChargeError(2, true, false, 2));
    }

    [Fact]
    public void Auto_enable_rejects_unowned_and_non_shot()
    {
        Assert.Equal(InventoryProtocol.NotOwned, ShotRules.AutoEnableError(false, false));
        Assert.Equal(ShotRules.NotAShot, ShotRules.AutoEnableError(true, false));
        Assert.Null(ShotRules.AutoEnableError(true, true));
    }
}
