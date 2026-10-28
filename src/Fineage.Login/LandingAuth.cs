using System.Text.RegularExpressions;

namespace Fineage.Login;

/// <summary>
/// Play-landing account rules shared with the Worker at fineage.evpoint.by.
/// Username: a letter, then letters, digits, or underscore, 3–24 characters, stored lowercase.
/// Email: stored lowercase, at most 254 characters.
/// Register password: 8–72 characters, no whitespace, not equal to the username or email.
/// </summary>
public static partial class LandingAuth
{
    public const string Mode = "live";

    [GeneratedRegex("^[a-z][a-z0-9_]{2,23}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();

    [GeneratedRegex("^[^\\s@]+@[^\\s@]+\\.[^\\s@]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    public static string? ValidateUsername(string? raw, out string canonical)
    {
        canonical = "";
        if (raw == null)
            return "invalid_username";
        string trimmed = raw.Trim().ToLowerInvariant();
        if (!UsernamePattern().IsMatch(trimmed))
            return "invalid_username";
        canonical = trimmed;
        return null;
    }

    public static string? ValidateEmail(string? raw, out string canonical)
    {
        canonical = "";
        if (raw == null)
            return "invalid_email";
        string trimmed = raw.Trim().ToLowerInvariant();
        if (trimmed.Length is < 1 or > 254 || !EmailPattern().IsMatch(trimmed))
            return "invalid_email";
        canonical = trimmed;
        return null;
    }

    public static string? RegisterPasswordError(string? password, string username, string email)
    {
        if (password == null || password.Length < 8 || password.Length > 72 || password != password.Trim() || HasWhitespace(password))
            return "invalid_password";
        string lower = password.ToLowerInvariant();
        if (lower == username || lower == email)
            return "password_matches_username";
        return null;
    }

    /// <summary>Login accepts shorter TCP passwords. Empty and over-long values are rejected before the store lookup.</summary>
    public static string? LoginPasswordError(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length > 72)
            return "invalid_password";
        return null;
    }

    public static string? LoginIdError(string? raw, out string canonical)
    {
        canonical = "";
        if (string.IsNullOrWhiteSpace(raw))
            return "invalid_username";
        string trimmed = raw.Trim().ToLowerInvariant();
        if (trimmed.Contains('@'))
            return ValidateEmail(trimmed, out canonical);
        return ValidateUsername(trimmed, out canonical);
    }

    static bool HasWhitespace(string password)
    {
        foreach (char ch in password)
        {
            if (char.IsWhiteSpace(ch))
                return true;
        }

        return false;
    }
}
