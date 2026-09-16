using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace OpenOTSTray.Services;

public class OneTimeSecretException : Exception
{
    public OneTimeSecretException(string message) : base(message) { }
}

public record CreatedSecret(string Link, string ReceiptIdentifier);

public class OneTimeSecretClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<CreatedSecret> CreateSecretLinkAsync(string secret, string region, string? passphrase = null, int ttl = 604800)
    {
        var shareDomain = $"{region}.onetimesecret.com";
        var url = $"https://{shareDomain}/api/v2/guest/secret/conceal";

        var secretPayload = new Dictionary<string, object?>
        {
            ["kind"] = "conceal",
            ["share_domain"] = shareDomain,
            ["secret"] = secret,
            ["ttl"] = ttl,
        };
        if (!string.IsNullOrEmpty(passphrase))
            secretPayload["passphrase"] = passphrase;

        var payload = new Dictionary<string, object?> { ["secret"] = secretPayload };

        using var response = await Http.PostAsJsonAsync(url, payload);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new OneTimeSecretException($"Failed to create secret ({(int)response.StatusCode}): {ExtractError(body)}");

        using var doc = JsonDocument.Parse(body);
        var record = doc.RootElement.GetProperty("record");
        var key = record.GetProperty("secret").GetProperty("key").GetString();
        var receiptIdentifier = record.GetProperty("receipt").GetProperty("identifier").GetString() ?? "";

        return new CreatedSecret($"https://{shareDomain}/secret/{key}", receiptIdentifier);
    }

    /// <summary>
    /// Fetches the receipt state ("new", "shared", "revealed", "burned", "previewed", "expired", "orphaned")
    /// for a secret created as a guest, via GET /api/v2/guest/receipt/{identifier} (no auth required).
    /// </summary>
    public async Task<string> GetReceiptStateAsync(string region, string receiptIdentifier)
    {
        var url = $"https://{region}.onetimesecret.com/api/v2/guest/receipt/{receiptIdentifier}";
        using var response = await Http.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new OneTimeSecretException($"Failed to fetch receipt status ({(int)response.StatusCode}): {ExtractError(body)}");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("record").GetProperty("state").GetString() ?? "unknown";
    }

    private static string ExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var errorProp))
                return errorProp.GetString() ?? body;
        }
        catch (JsonException)
        {
            // response body wasn't JSON; fall back to the raw text below
        }
        return body;
    }
}
