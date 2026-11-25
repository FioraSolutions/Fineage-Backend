namespace Fineage.Protocol;

/// <summary>Return from the world to the character lobby. Retail <c>RequestRestart</c> / <c>RestartResponse</c>.</summary>
public static class RestartProtocol
{
    public const string NoAccount = "restart_account";
    public const string NotInWorld = "restart_not_in_world";
    public const string Enchant = "restart_enchant";
    public const string Store = "restart_store";
    public const string Combat = "restart_combat";
    public const string Festival = "restart_festival";
}
