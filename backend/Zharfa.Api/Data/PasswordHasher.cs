namespace Zharfa.Api.Data;

using System;
using System.Security.Cryptography;

/// <summary>
/// Password hashing and verification using PBKDF2-HMAC-SHA256.
/// Format: pbkdf2-sha256$&lt;iterations&gt;$&lt;base64 salt&gt;$&lt;base64 hash&gt;
/// Never log passwords or password hashes.
/// </summary>
public class PasswordHasher
{
    private const int Iterations = 100000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string AlgorithmPrefix = "pbkdf2-sha256";

    public static string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);

        return $"{AlgorithmPrefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(stored))
        {
            return false;
        }

        string[] parts = stored.Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], AlgorithmPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out int iterations) || iterations <= 0)
        {
            return false;
        }

        try
        {
            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] expectedHash = Convert.FromBase64String(parts[3]);

            byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch
        {
            return false;
        }
    }
}
