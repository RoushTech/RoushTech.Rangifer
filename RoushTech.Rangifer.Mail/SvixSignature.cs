namespace RoushTech.Rangifer.Mail;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// <summary>Resend signs webhooks the Svix way.</summary>
public static class SvixSignature
{
    public const string IdHeader = "svix-id";
    public const string TimestampHeader = "svix-timestamp";
    public const string SignatureHeader = "svix-signature";

    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public static string Sign(string secret, string id, string timestamp, string body) =>
        Convert.ToBase64String(HMACSHA256.HashData(Key(secret), Encoding.UTF8.GetBytes($"{id}.{timestamp}.{body}")));

    public static bool IsValid(string secret, string? id, string? timestamp, string? signatures, string body, DateTimeOffset now) =>
        Problem(secret, id, timestamp, signatures, body, now) is null;

    /// <returns>Why the webhook was refused, for logging, or null when it's genuine.</returns>
    public static string? Problem(string secret, string? id, string? timestamp, string? signatures, string body, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(signatures) || string.IsNullOrEmpty(timestamp)
            || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return $"missing {IdHeader}, {TimestampHeader} or {SignatureHeader}";
        }

        if (seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return "timestamp is out of range";
        }

        var skew = (now - DateTimeOffset.FromUnixTimeSeconds(seconds)).Duration();
        if (skew > Tolerance)
        {
            return $"timestamp is {skew.TotalSeconds.ToString("N0", CultureInfo.InvariantCulture)} seconds off";
        }

        byte[] expected;
        try
        {
            expected = Encoding.ASCII.GetBytes(Sign(secret, id, timestamp, body));
        }
        catch (FormatException)
        {
            return "the configured secret isn't valid base64";
        }

        var matches = signatures.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => s.StartsWith("v1,", StringComparison.Ordinal))
            .Any(s => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(s["v1,".Length..]), expected));
        return matches ? null : "signature doesn't match the configured secret";
    }

    private static byte[] Key(string secret) =>
        Convert.FromBase64String(secret.StartsWith("whsec_", StringComparison.Ordinal) ? secret["whsec_".Length..] : secret);
}
