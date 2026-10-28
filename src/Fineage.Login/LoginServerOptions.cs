namespace Fineage.Login;

public sealed class LoginServerOptions
{
    public int MaxMessagesPerSecond { get; init; } = LoginNetSettings.DefaultMaxMessagesPerSecond;

    /// <summary>Minimum gap between character selects on one login session. Zero disables the flood gate.</summary>
    public TimeSpan SelectMinInterval { get; init; } = TimeSpan.FromSeconds(1);
}
