namespace Fineage.Login;

public sealed class LoginServerOptions
{
    public int MaxMessagesPerSecond { get; init; } = LoginNetSettings.DefaultMaxMessagesPerSecond;
}
