using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Wwsrapport;

public sealed class WwsrapportClient : IDisposable
{
    public const string DefaultBaseUrl = "https://wwsrapport.nl/v1";
    private const string ClientHeaderValue = "wwsrapport-dotnet/0.2.0";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;

    public WwsrapportClient(string apiKey, string? baseUrl = null, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("WWSrapport API key is required.", nameof(apiKey));
        }

        _httpClient = httpClient ?? new HttpClient();
        _disposeHttpClient = httpClient is null;

        BaseUrl = NormalizeBaseUrl(baseUrl ?? DefaultBaseUrl);
        ApiKey = apiKey;

        Properties = new PropertiesResource(this);
        Reports = new ReportsResource(this);
        Documents = new DocumentsResource(this);
        Usage = new UsageResource(this);
        Rulesets = new RulesetsResource(this);
        Webhooks = new WebhooksResource(this);
        Registry = new RegistryResource(this);
    }

    public string ApiKey { get; }
    public Uri BaseUrl { get; }
    public PropertiesResource Properties { get; }
    public ReportsResource Reports { get; }
    public DocumentsResource Documents { get; }
    public UsageResource Usage { get; }
    public RulesetsResource Rulesets { get; }
    public WebhooksResource Webhooks { get; }
    public RegistryResource Registry { get; }

    internal async Task<T?> RequestJsonAsync<T>(
        HttpMethod method,
        string path,
        object? body = null,
        IReadOnlyDictionary<string, object?>? query = null,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default
    )
    {
        using var request = BuildRequest(method, path, query, headers);

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseBody = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            ThrowApiError(response, responseBody);
        }

        if (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(responseBody))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
    }

    internal async Task<byte[]> RequestBinaryAsync(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, object?>? query = null,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default
    )
    {
        using var request = BuildRequest(method, path, query, headers);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/pdf"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = response.Content is null
                ? string.Empty
                : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            ThrowApiError(response, responseBody);
        }

        return response.Content is null
            ? Array.Empty<byte>()
            : await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private HttpRequestMessage BuildRequest(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, object?>? query,
        IReadOnlyDictionary<string, string>? headers
    )
    {
        var request = new HttpRequestMessage(method, BuildUri(path, query));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        request.Headers.TryAddWithoutValidation("X-WWSrapport-Client", ClientHeaderValue);

        foreach (var (key, value) in (IEnumerable<KeyValuePair<string, string>>?)headers ?? Array.Empty<KeyValuePair<string, string>>())
        {
            request.Headers.TryAddWithoutValidation(key, value);
        }

        return request;
    }

    private Uri BuildUri(string path, IReadOnlyDictionary<string, object?>? query)
    {
        var relativePath = path.TrimStart('/');
        var builder = new UriBuilder(new Uri(BaseUrl, relativePath));

        if (query is not null)
        {
            var queryParts = query
                .Where(pair => pair.Value is not null && pair.Value.ToString() is { Length: > 0 })
                .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!.ToString()!)}");

            builder.Query = string.Join("&", queryParts);
        }

        return builder.Uri;
    }

    private static Uri NormalizeBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("WWSrapport base URL must be an absolute URL.", nameof(baseUrl));
        }

        return uri;
    }

    private static void ThrowApiError(HttpResponseMessage response, string responseBody)
    {
        JsonObject? problem = null;

        try
        {
            problem = string.IsNullOrWhiteSpace(responseBody) ? null : JsonNode.Parse(responseBody)?.AsObject();
        }
        catch (JsonException)
        {
            problem = null;
        }

        var message = ReadProblemMessage(problem) ?? $"WWSrapport API request failed with status {(int)response.StatusCode}.";
        var requestId = TryGetHeader(response, "X-Request-Id") ?? TryGetHeader(response, "x-request-id");

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new WwsrapportAuthenticationException(message, response.StatusCode, problem, responseBody, requestId),
            HttpStatusCode.PaymentRequired => new WwsrapportPaymentRequiredException(message, response.StatusCode, problem, responseBody, requestId),
            HttpStatusCode.Forbidden => new WwsrapportForbiddenException(message, response.StatusCode, problem, responseBody, requestId),
            HttpStatusCode.NotFound => new WwsrapportNotFoundException(message, response.StatusCode, problem, responseBody, requestId),
            HttpStatusCode.Conflict => new WwsrapportConflictException(message, response.StatusCode, problem, responseBody, requestId),
            HttpStatusCode.UnprocessableEntity => new WwsrapportValidationException(message, response.StatusCode, problem, responseBody, requestId),
            (HttpStatusCode)429 => new WwsrapportRateLimitException(message, response.StatusCode, problem, responseBody, requestId),
            _ => new WwsrapportException(message, response.StatusCode, problem, responseBody, requestId),
        };
    }

    private static string? ReadProblemMessage(JsonObject? problem)
    {
        if (problem is null)
        {
            return null;
        }

        foreach (var key in new[] { "detail", "message", "title" })
        {
            if (problem.TryGetPropertyValue(key, out var value) && value is not null)
            {
                return value.ToString();
            }
        }

        return null;
    }

    private static string? TryGetHeader(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values))
        {
            return values.FirstOrDefault();
        }

        return response.Content?.Headers.TryGetValues(name, out values) == true ? values.FirstOrDefault() : null;
    }

    public void Dispose()
    {
        if (_disposeHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}

public sealed class PropertiesResource
{
    private readonly WwsrapportClient _client;

    internal PropertiesResource(WwsrapportClient client) => _client = client;

    public Task<ApiEnvelope<PropertyPrefill>?> PrefillAsync(AddressInput address, CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<ApiEnvelope<PropertyPrefill>>(HttpMethod.Post, "/properties/prefill", new { address }, cancellationToken: cancellationToken);
}

public sealed class ReportsResource
{
    private readonly WwsrapportClient _client;

    internal ReportsResource(WwsrapportClient client) => _client = client;

    public Task<ApiEnvelope<ReportValidationResult>?> ValidateAsync(JsonObject input, CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<ApiEnvelope<ReportValidationResult>>(HttpMethod.Post, "/reports/validate", input, cancellationToken: cancellationToken);

    public Task<ApiEnvelope<ReportSummary>?> CreateAsync(ReportCreateInput input, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency key is required when creating a report.", nameof(idempotencyKey));
        }

        return _client.RequestJsonAsync<ApiEnvelope<ReportSummary>>(
            HttpMethod.Post,
            "/reports",
            input,
            headers: new Dictionary<string, string> { ["Idempotency-Key"] = idempotencyKey },
            cancellationToken: cancellationToken
        );
    }

    public Task<ApiEnvelope<List<ReportSummary>>?> ListAsync(IReadOnlyDictionary<string, object?>? query = null, CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<ApiEnvelope<List<ReportSummary>>>(HttpMethod.Get, "/reports", query: query, cancellationToken: cancellationToken);

    public Task<ApiEnvelope<ReportSummary>?> GetAsync(string reportId, CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<ApiEnvelope<ReportSummary>>(HttpMethod.Get, $"/reports/{Uri.EscapeDataString(reportId)}", cancellationToken: cancellationToken);

    public Task<ApiEnvelope<CalculationResult>?> CalculationAsync(string reportId, CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<ApiEnvelope<CalculationResult>>(HttpMethod.Get, $"/reports/{Uri.EscapeDataString(reportId)}/calculation", cancellationToken: cancellationToken);

    public Task<ApiEnvelope<ImprovementAdvice>?> ImprovementAdviceAsync(string reportId, CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<ApiEnvelope<ImprovementAdvice>>(HttpMethod.Get, $"/reports/{Uri.EscapeDataString(reportId)}/improvement-advice", cancellationToken: cancellationToken);

    public Task<JsonObject?> VerificationAsync(string reportId, CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<JsonObject>(HttpMethod.Get, $"/reports/{Uri.EscapeDataString(reportId)}/verification", cancellationToken: cancellationToken);
}

public sealed class RegistryResource
{
    private readonly WwsrapportClient _client;
    internal RegistryResource(WwsrapportClient client) => _client = client;

    public Task<JsonObject?> DeriveBagReferenceAsync(string bagVboId, CancellationToken cancellationToken = default)
    {
        Validate(bagVboId);
        return _client.RequestJsonAsync<JsonObject>(HttpMethod.Post, "/registry/bag-reference", new { bagVboId }, cancellationToken: cancellationToken);
    }

    public Task<JsonObject?> SearchByBagAsync(string bagVboId, CancellationToken cancellationToken = default)
    {
        Validate(bagVboId);
        return _client.RequestJsonAsync<JsonObject>(HttpMethod.Post, "/registry/search-by-bag", new { bagVboId }, cancellationToken: cancellationToken);
    }

    private static void Validate(string value)
    {
        if (value.Length != 16 || value.Any(character => character < '0' || character > '9'))
            throw new ArgumentException("BAG verblijfsobject ID must contain exactly sixteen digits.", nameof(value));
    }
}

public sealed class DocumentsResource
{
    private readonly WwsrapportClient _client;

    internal DocumentsResource(WwsrapportClient client) => _client = client;

    public Task<ApiEnvelope<List<DocumentSummary>>?> ListAsync(string reportId, CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<ApiEnvelope<List<DocumentSummary>>>(HttpMethod.Get, $"/reports/{Uri.EscapeDataString(reportId)}/documents", cancellationToken: cancellationToken);

    public Task<byte[]> DownloadWwsReportAsync(string reportId, CancellationToken cancellationToken = default)
        => _client.RequestBinaryAsync(HttpMethod.Get, $"/reports/{Uri.EscapeDataString(reportId)}/documents/wws-report", cancellationToken: cancellationToken);

    public Task<byte[]> DownloadImprovementAdviceAsync(string reportId, CancellationToken cancellationToken = default)
        => _client.RequestBinaryAsync(HttpMethod.Get, $"/reports/{Uri.EscapeDataString(reportId)}/documents/improvement-advice", cancellationToken: cancellationToken);
}

public sealed class UsageResource
{
    private readonly WwsrapportClient _client;

    internal UsageResource(WwsrapportClient client) => _client = client;

    public Task<ApiEnvelope<UsageSummary>?> CurrentAsync(CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<ApiEnvelope<UsageSummary>>(HttpMethod.Get, "/usage/current", cancellationToken: cancellationToken);

    public Task<ApiEnvelope<List<UsageSummary>>?> HistoryAsync(IReadOnlyDictionary<string, object?>? query = null, CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<ApiEnvelope<List<UsageSummary>>>(HttpMethod.Get, "/usage/history", query: query, cancellationToken: cancellationToken);
}

public sealed class RulesetsResource
{
    private readonly WwsrapportClient _client;

    internal RulesetsResource(WwsrapportClient client) => _client = client;

    public Task<ApiEnvelope<List<RulesetSummary>>?> ListAsync(CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<ApiEnvelope<List<RulesetSummary>>>(HttpMethod.Get, "/rulesets", cancellationToken: cancellationToken);
}

public sealed class WebhooksResource
{
    private readonly WwsrapportClient _client;

    internal WebhooksResource(WwsrapportClient client) => _client = client;

    public Task<ApiEnvelope<List<WebhookEndpoint>>?> ListAsync(CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<ApiEnvelope<List<WebhookEndpoint>>>(HttpMethod.Get, "/webhooks", cancellationToken: cancellationToken);

    public Task<ApiEnvelope<WebhookEndpoint>?> CreateAsync(WebhookCreateInput input, CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<ApiEnvelope<WebhookEndpoint>>(HttpMethod.Post, "/webhooks", input, cancellationToken: cancellationToken);

    public Task<ApiEnvelope<WebhookEndpoint>?> GetAsync(string webhookId, CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<ApiEnvelope<WebhookEndpoint>>(HttpMethod.Get, $"/webhooks/{Uri.EscapeDataString(webhookId)}", cancellationToken: cancellationToken);

    public Task<ApiEnvelope<WebhookEndpoint>?> UpdateAsync(string webhookId, WebhookUpdateInput input, CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<ApiEnvelope<WebhookEndpoint>>(HttpMethod.Patch, $"/webhooks/{Uri.EscapeDataString(webhookId)}", input, cancellationToken: cancellationToken);

    public Task DeleteAsync(string webhookId, CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<EmptyResponse>(HttpMethod.Delete, $"/webhooks/{Uri.EscapeDataString(webhookId)}", cancellationToken: cancellationToken);

    public Task<ApiEnvelope<WebhookDelivery>?> SendTestAsync(string webhookId, CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<ApiEnvelope<WebhookDelivery>>(HttpMethod.Post, $"/webhooks/{Uri.EscapeDataString(webhookId)}/test", cancellationToken: cancellationToken);

    public Task<ApiEnvelope<List<WebhookDelivery>>?> DeliveriesAsync(string webhookId, IReadOnlyDictionary<string, object?>? query = null, CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<ApiEnvelope<List<WebhookDelivery>>>(HttpMethod.Get, $"/webhooks/{Uri.EscapeDataString(webhookId)}/deliveries", query: query, cancellationToken: cancellationToken);

    public Task<ApiEnvelope<WebhookDelivery>?> RetryDeliveryAsync(string webhookId, string deliveryId, CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<ApiEnvelope<WebhookDelivery>>(HttpMethod.Post, $"/webhooks/{Uri.EscapeDataString(webhookId)}/deliveries/{Uri.EscapeDataString(deliveryId)}/retry", cancellationToken: cancellationToken);
}
