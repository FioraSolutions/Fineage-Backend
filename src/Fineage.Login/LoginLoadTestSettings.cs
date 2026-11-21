namespace Fineage.Login;

/// <summary>
/// Gates auto-registration on login Net <c>Auth.RegisterIfMissing</c> to configured load-test account names.
/// Production leaves prefix/password unset so the flag is ignored.
/// </summary>
public static class LoginLoadTestSettings
{
    public const string PrefixVariable = "FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX";
    public const string PasswordVariable = "FINEAGE_LOGIN_LOADTEST_PASSWORD";

    public static string? AccountPrefix => NormalizePrefix(Environment.GetEnvironmentVariable(PrefixVariable));
    public static string? Password => NormalizePassword(Environment.GetEnvironmentVariable(PasswordVariable));

    /// <summary>
    /// When <paramref name="registerIfMissing"/> is false, returns true (normal login path).
    /// When true, registration is allowed only if prefix and password env are set and match the request.
    /// </summary>
    public static bool AllowsRegisterIfMissing(string? account, string? password, bool registerIfMissing)
    {
        if (!registerIfMissing)
            return true;

        string? prefix = AccountPrefix;
        string? expectedPassword = Password;
        if (prefix == null || expectedPassword == null)
            return false;

        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
            return false;

        return account.StartsWith(prefix, StringComparison.Ordinal)
            && string.Equals(password, expectedPassword, StringComparison.Ordinal);
    }

    static string? NormalizePrefix(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        text = text.Trim();
        return text.Length == 0 ? null : text;
    }

    static string? NormalizePassword(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        return text;
    }
}