namespace Fineage.Game.RequiredData;

/// <summary>How a missing required row is handled.</summary>
public enum RequiredDataPolicy
{
    /// <summary>Zone/Login boot refuses to start when the catalog file or pack is absent.</summary>
    FailFastBoot,

    /// <summary>The specific action that needs the row refuses and names the missing key.</summary>
    RefuseAction
}
