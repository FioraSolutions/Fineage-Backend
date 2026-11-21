using Xunit;

namespace Fineage.Login.Tests;

public sealed class LoginLoadTestSettingsTests
{
    [Fact]
    public void RegisterIfMissing_denied_when_prefix_unset()
    {
        using var _ = EnvScope.Clear(LoginLoadTestSettings.PrefixVariable, LoginLoadTestSettings.PasswordVariable);
        Assert.False(LoginLoadTestSettings.AllowsRegisterIfMissing("loadbot1", "loadtest-pass-v1", registerIfMissing: true));
    }

    [Fact]
    public void RegisterIfMissing_allowed_when_env_matches()
    {
        using var prefix = EnvScope.Set(LoginLoadTestSettings.PrefixVariable, "loadbot");
        using var password = EnvScope.Set(LoginLoadTestSettings.PasswordVariable, "loadtest-pass-v1");
        Assert.True(LoginLoadTestSettings.AllowsRegisterIfMissing("loadbot0001", "loadtest-pass-v1", registerIfMissing: true));
        Assert.False(LoginLoadTestSettings.AllowsRegisterIfMissing("other1", "loadtest-pass-v1", registerIfMissing: true));
        Assert.False(LoginLoadTestSettings.AllowsRegisterIfMissing("loadbot0001", "wrong", registerIfMissing: true));
    }
}

file static class EnvScope
{
    public static IDisposable Set(string name, string value)
    {
        string? previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        return new Reset(name, previous);
    }

    public static IDisposable Clear(params string[] names)
    {
        var previous = names.Select(n => (n, Environment.GetEnvironmentVariable(n))).ToArray();
        foreach (string name in names)
            Environment.SetEnvironmentVariable(name, null);
        return new ResetMany(previous);
    }

    sealed class Reset(string name, string? previous) : IDisposable
    {
        public void Dispose() => Environment.SetEnvironmentVariable(name, previous);
    }

    sealed class ResetMany((string name, string? previous)[] entries) : IDisposable
    {
        public void Dispose()
        {
            foreach (var (name, previous) in entries)
                Environment.SetEnvironmentVariable(name, previous);
        }
    }
}