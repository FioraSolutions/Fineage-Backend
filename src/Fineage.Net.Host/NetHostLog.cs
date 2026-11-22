namespace Fineage.Net.Host;

static class NetHostLog
{
    public static void Warn(string message) =>
        Console.Error.WriteLine($"{DateTimeOffset.UtcNow:O} warn: {message}");
}