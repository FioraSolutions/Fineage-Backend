using System.Security.Cryptography;

namespace Fineage.Agent.Identity;

/// <summary>
/// Hashes and verifies Agent integration secrets. The plaintext secret is never stored.
/// This hasher is not the game-account password hasher.
/// </summary>
public static class AgentSecretHasher
{
    public const int Iterations = 10_000;
    public const int MinIterations = 10_000;
    public const int MaxIterations = 200_000;
    const int SaltBytes = 16;
    const int HashBytes = 32;
    const int SecretBytes = 32;

    public static string NewSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes));

    public static string NewId(string prefix) => prefix + Guid.NewGuid().ToString("N");

    public static (string Hash, string Salt, int Iterations) Hash(string secret)
    {
        if (string.IsNullOrEmpty(secret))
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "A secret is required.");
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(secret, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt), Iterations);
    }

    public static bool Verify(string secret, string hash, string salt, int iterations)
    {
        if (string.IsNullOrEmpty(secret) || !IterationsAllowed(iterations))
            return false;
        byte[] expected;
        byte[] saltBytes;
        try
        {
            expected = Convert.FromBase64String(hash);
            saltBytes = Convert.FromBase64String(salt);
        }
        catch (FormatException)
        {
            return false;
        }

        if (expected.Length != HashBytes || saltBytes.Length != SaltBytes)
            return false;

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(secret, saltBytes, iterations, HashAlgorithmName.SHA256, HashBytes);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static bool IterationsAllowed(int iterations) => iterations is >= MinIterations and <= MaxIterations;
}
