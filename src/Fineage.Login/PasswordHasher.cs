using System.Security.Cryptography;

namespace Fineage.Login;

public static class PasswordHasher
{
    public const int Iterations = 10_000;
    const int SaltBytes = 16;
    const int HashBytes = 32;

    public static (string Hash, string Salt, int Iterations) Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt), Iterations);
    }

    public static bool Verify(string password, string hash, string salt, int iterations)
    {
        if (!IterationsAllowed(iterations))
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

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, iterations, HashAlgorithmName.SHA256, HashBytes);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static bool IterationsAllowed(int iterations) => iterations is >= 10_000 and <= 200_000;
}
