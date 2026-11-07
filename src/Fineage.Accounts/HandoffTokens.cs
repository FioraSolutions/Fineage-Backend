using System.Net;
using System.Security.Cryptography;
using System.Text;
using Fineage.Protocol;

namespace Fineage.Accounts;

public sealed record HandoffClaims(
    string Account,
    int CharacterId,
    string CharacterName,
    int ClassId,
    int Race,
    int Sex,
    long ExpiresUnixSeconds);

public sealed record HandoffTicket(
    string Account,
    int CharacterId,
    string CharacterName,
    int ClassId,
    int Race,
    int Sex,
    string Token,
    long ExpiresUnixSeconds,
    string ZoneHost,
    int ZonePort)
{
    public AuthMsg ToZoneAuth() => new() { Account = Account, Password = Token };
}

/// <summary>
/// Opaque session key the client places in <see cref="AuthMsg.Password"/>.
/// Format: <c>fa1.&lt;base64url payload&gt;.&lt;base64url hmac-sha256&gt;</c>.
/// </summary>
public sealed class HandoffTokens
{
    public const string DefaultSecret = "FineageLoginHandoffV1";
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

    public enum ValidateStatus
    {
        Ok,
        Invalid,
        Expired,
    }

    readonly byte[] _key;
    readonly TimeSpan _ttl;

    public HandoffTokens(string secret, TimeSpan? ttl = null)
    {
        if (string.IsNullOrEmpty(secret))
            throw new ArgumentException("handoff secret is required", nameof(secret));
        _key = Encoding.UTF8.GetBytes(secret);
        _ttl = ttl ?? DefaultTtl;
        if (_ttl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ttl));
    }

    /// <summary>
    /// Builds tokens from <c>FINEAGE_HANDOFF_SECRET</c> (+ optional TTL env).
    /// When the secret is unset, <paramref name="allowDefaultSecret"/> must be true
    /// (loopback / selftest only); otherwise this throws so production cannot ship
    /// with the well-known <see cref="DefaultSecret"/>.
    /// </summary>
    public static HandoffTokens FromEnvironment(bool allowDefaultSecret = false)
    {
        string? secret = Environment.GetEnvironmentVariable("FINEAGE_HANDOFF_SECRET");
        if (string.IsNullOrWhiteSpace(secret))
        {
            if (!allowDefaultSecret || RequireConfiguredSecret())
                throw new InvalidOperationException(
                    "FINEAGE_HANDOFF_SECRET is required (shared Login↔Zone HMAC). " +
                    "Set a random secret (≥32 bytes). DefaultSecret is for tests/loopback only.");
            secret = DefaultSecret;
        }

        TimeSpan? ttl = null;
        string? ttlText = Environment.GetEnvironmentVariable("FINEAGE_HANDOFF_TTL_SECONDS");
        if (int.TryParse(ttlText, out int seconds) && seconds > 0)
            ttl = TimeSpan.FromSeconds(seconds);

        return new HandoffTokens(secret, ttl);
    }

    /// <summary>
    /// True when <c>FINEAGE_REQUIRE_HANDOFF_SECRET</c> is 1/true — forces a configured
    /// secret even on loopback binds.
    /// </summary>
    public static bool RequireConfiguredSecret()
    {
        string? flag = Environment.GetEnvironmentVariable("FINEAGE_REQUIRE_HANDOFF_SECRET");
        return flag is "1" or "true" or "TRUE" or "yes" or "YES";
    }

    /// <summary>Whether this bind may fall back to <see cref="DefaultSecret"/>.</summary>
    public static bool AllowDefaultSecretForBind(IPAddress bindAddress) =>
        !RequireConfiguredSecret() && IPAddress.IsLoopback(bindAddress);

    public HandoffTicket Issue(CharacterRecord character, DateTimeOffset now)
    {
        long exp = now.ToUnixTimeSeconds() + (long)_ttl.TotalSeconds;
        string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        string payload = string.Join('|',
            "1",
            character.Account,
            character.Id.ToString(),
            character.Name,
            character.ClassId.ToString(),
            character.Race.ToString(),
            character.Sex.ToString(),
            exp.ToString(),
            nonce);
        byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
        string token = "fa1." + B64(payloadBytes) + "." + B64(Sign(payloadBytes));
        return new HandoffTicket(
            character.Account,
            character.Id,
            character.Name,
            character.ClassId,
            character.Race,
            character.Sex,
            token,
            exp,
            ZoneHandoff.AdvertiseHost,
            ZoneHandoff.AdvertisePort);
    }

    public bool TryValidate(string token, DateTimeOffset now, out HandoffClaims claims) =>
        Validate(token, now, out claims) == ValidateStatus.Ok;

    public ValidateStatus Validate(string token, DateTimeOffset now, out HandoffClaims claims)
    {
        claims = new HandoffClaims("", 0, "", 0, 0, 0, 0);
        if (string.IsNullOrEmpty(token))
            return ValidateStatus.Invalid;
        string[] parts = token.Split('.');
        if (parts.Length != 3 || parts[0] != "fa1")
            return ValidateStatus.Invalid;

        byte[] payload;
        byte[] mac;
        try
        {
            payload = B64Decode(parts[1]);
            mac = B64Decode(parts[2]);
        }
        catch (FormatException)
        {
            return ValidateStatus.Invalid;
        }

        byte[] expected = Sign(payload);
        if (mac.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(mac, expected))
            return ValidateStatus.Invalid;

        string text = Encoding.UTF8.GetString(payload);
        string[] fields = text.Split('|');
        if (fields.Length != 9 || fields[0] != "1")
            return ValidateStatus.Invalid;
        if (!int.TryParse(fields[2], out int id) ||
            !int.TryParse(fields[4], out int classId) ||
            !int.TryParse(fields[5], out int race) ||
            !int.TryParse(fields[6], out int sex) ||
            !long.TryParse(fields[7], out long exp))
            return ValidateStatus.Invalid;
        if (now.ToUnixTimeSeconds() >= exp)
            return ValidateStatus.Expired;

        claims = new HandoffClaims(fields[1], id, fields[3], classId, race, sex, exp);
        return ValidateStatus.Ok;
    }

    byte[] Sign(byte[] payload)
    {
        using var hmac = new HMACSHA256(_key);
        return hmac.ComputeHash(payload);
    }

    static string B64(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static byte[] B64Decode(string text)
    {
        string s = text.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }

        return Convert.FromBase64String(s);
    }
}

public static class ZoneHandoff
{
    public const string Host = "127.0.0.1";
    public const int Port = 9777;

    /// <summary>Host the client should dial. <c>FINEAGE_ZONE_HOST</c>, otherwise <see cref="Host"/>.</summary>
    public static string AdvertiseHost
    {
        get
        {
            string? host = Environment.GetEnvironmentVariable("FINEAGE_ZONE_HOST");
            return string.IsNullOrWhiteSpace(host) ? Host : host.Trim();
        }
    }

    /// <summary>
    /// Zone TCP port written into the handoff ticket.
    /// <c>FINEAGE_ZONE_PORT</c> wins, then <c>FINEAGE_NET_ZONE_PORT</c>, then 9777.
    /// Kept free of <c>Fineage.Net.Host</c> so Accounts stays a shared library for Login and Zone.
    /// </summary>
    public static int AdvertisePort
    {
        get
        {
            foreach (string? text in new[]
                     {
                         Environment.GetEnvironmentVariable("FINEAGE_ZONE_PORT"),
                         Environment.GetEnvironmentVariable("FINEAGE_NET_ZONE_PORT"),
                     })
            {
                if (int.TryParse(text, out int port) && port is >= 1 and <= 65535)
                    return port;
            }

            return 9777;
        }
    }
}
