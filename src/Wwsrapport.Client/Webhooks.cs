using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Wwsrapport.Webhooks;

public static class WebhookSignatureVerifier
{
    private const string TimestampHeader = "WWS-Webhook-Timestamp";
    private const string SignatureHeader = "WWS-Webhook-Signature";
    private static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    public static bool Verify(
        string payload,
        IReadOnlyDictionary<string, string> headers,
        string secret,
        TimeSpan? tolerance = null,
        DateTimeOffset? now = null
    )
    {
        if (string.IsNullOrEmpty(payload) || string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        var timestamp = GetHeader(headers, TimestampHeader);
        var signature = GetHeader(headers, SignatureHeader);

        if (string.IsNullOrWhiteSpace(timestamp) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        if (!long.TryParse(timestamp, out var timestampSeconds))
        {
            return false;
        }

        var timestampValue = DateTimeOffset.FromUnixTimeSeconds(timestampSeconds);
        var currentTime = now ?? DateTimeOffset.UtcNow;

        if ((currentTime - timestampValue).Duration() > (tolerance ?? DefaultTolerance))
        {
            return false;
        }

        var expected = ComputeSignature(timestamp, payload, secret);

        foreach (var part in signature.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var signatureParts = part.Split('=', 2);

            if (signatureParts.Length != 2 || signatureParts[0] != "v1")
            {
                continue;
            }

            if (FixedTimeEquals(expected, signatureParts[1]))
            {
                return true;
            }
        }

        return false;
    }

    public static Wwsrapport.WebhookEvent<JsonElement>? ParseEvent(string payload)
        => JsonSerializer.Deserialize<Wwsrapport.WebhookEvent<JsonElement>>(payload, JsonOptions);

    public static Wwsrapport.WebhookEvent<T>? ParseEvent<T>(string payload)
        => JsonSerializer.Deserialize<Wwsrapport.WebhookEvent<T>>(payload, JsonOptions);

    public static string ComputeSignature(string timestamp, string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? GetHeader(IReadOnlyDictionary<string, string> headers, string name)
    {
        foreach (var (key, value) in headers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    private static bool FixedTimeEquals(string expectedHex, string actualHex)
    {
        if (expectedHex.Length != actualHex.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedHex),
            Encoding.UTF8.GetBytes(actualHex.ToLowerInvariant())
        );
    }
}
