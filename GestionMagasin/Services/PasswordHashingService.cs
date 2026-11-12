using System.Security.Cryptography;

namespace GestionMagasin.Services;

/// <summary>
/// Password storage format: pbkdf2-sha256$600000$&lt;base64 16-byte salt&gt;$&lt;base64 32-byte key&gt;.
/// PBKDF2-SHA256 uses 600,000 iterations (OWASP recommendation), a cryptographically random salt,
/// and a 256-bit derived key. The format carries its algorithm and work factor for future upgrades.
/// </summary>
public sealed class PasswordHashingService
{
    public const int Iterations = 600_000;
    private const int SaltSizeBytes = 16;
    private const int KeySizeBytes = 32;
    private const string Algorithm = "pbkdf2-sha256";

    public string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySizeBytes);
        return string.Join('$', Algorithm, Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(key));
    }

    public bool VerifyPassword(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        var parts = storedHash.Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], Algorithm, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations) || iterations is < 100_000 or > 2_000_000)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            if (salt.Length != SaltSizeBytes || expected.Length != KeySizeBytes)
            {
                return false;
            }

            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, KeySizeBytes);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
