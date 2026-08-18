using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Wwsrapport;

public sealed class WwsrapportClient : IDisposable
{
    public const string DefaultBaseUrl = "https://wwsrapport.nl/v1";
    private const string ClientHeaderValue = "wwsrapport-dotnet/0.3.0";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;
    private readonly OAuthClientCredentialsOptions? _oauth;
    private readonly SemaphoreSlim _oauthLock = new(1, 1);
    private string? _oauthToken;
    private DateTimeOffset _oauthExpiresAt;

    public WwsrapportClient(string apiKey, string? baseUrl = null, HttpClient? httpClient = null, PublicSectorRequestContext? requestContext = null, string apiVersion = "1.2.0")
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("WWSrapport API key is required.", nameof(apiKey));
        }

        _httpClient = httpClient ?? new HttpClient();
        _disposeHttpClient = httpClient is null;

        BaseUrl = NormalizeBaseUrl(baseUrl ?? DefaultBaseUrl);
        ApiKey = apiKey;
        RequestContext = requestContext;
        ApiVersion = apiVersion;

        InitializeResources();
    }

    public WwsrapportClient(OAuthClientCredentialsOptions oauth, string? baseUrl = null, HttpClient? httpClient = null, PublicSectorRequestContext? requestContext = null, string apiVersion = "1.2.0")
    {
        if (string.IsNullOrWhiteSpace(oauth.ClientId) || string.IsNullOrWhiteSpace(oauth.ClientSecret))
            throw new ArgumentException("OAuth client id and secret are required.", nameof(oauth));
        _httpClient = httpClient ?? new HttpClient();
        _disposeHttpClient = httpClient is null;
        BaseUrl = NormalizeBaseUrl(baseUrl ?? DefaultBaseUrl);
        _oauth = oauth;
        RequestContext = requestContext;
        ApiVersion = apiVersion;

        InitializeResources();
    }

    private void InitializeResources()
    {

        Properties = new PropertiesResource(this);
        Reports = new ReportsResource(this);
        Documents = new DocumentsResource(this);
        Usage = new UsageResource(this);
        Rulesets = new RulesetsResource(this);
        Webhooks = new WebhooksResource(this);
        Registry = new RegistryResource(this);
        Batches = new BatchesResource(this);
        TenantLifecycle = new TenantLifecycleResource(this);
    }

    public string? ApiKey { get; }
    public Uri BaseUrl { get; }
    public PublicSectorRequestContext? RequestContext { get; }
    public string ApiVersion { get; }
    public PropertiesResource Properties { get; private set; } = null!;
    public ReportsResource Reports { get; private set; } = null!;
    public DocumentsResource Documents { get; private set; } = null!;
    public UsageResource Usage { get; private set; } = null!;
    public RulesetsResource Rulesets { get; private set; } = null!;
    public WebhooksResource Webhooks { get; private set; } = null!;
    public RegistryResource Registry { get; private set; } = null!;
    public BatchesResource Batches { get; private set; } = null!;
    public TenantLifecycleResource TenantLifecycle { get; private set; } = null!;

    internal async Task<T?> RequestJsonAsync<T>(
        HttpMethod method,
        string path,
        object? body = null,
        IReadOnlyDictionary<string, object?>? query = null,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default
    )
    {
        using var request = await BuildRequestAsync(method, path, query, headers, cancellationToken).ConfigureAwait(false);

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
        using var request = await BuildRequestAsync(method, path, query, headers, cancellationToken).ConfigureAwait(false);
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

    private async Task<HttpRequestMessage> BuildRequestAsync(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, object?>? query,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken
    )
    {
        var request = new HttpRequestMessage(method, BuildUri(path, query));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(cancellationToken).ConfigureAwait(false));
        request.Headers.TryAddWithoutValidation("X-WWSrapport-Client", ClientHeaderValue);
        var requestId = $"req_{Guid.NewGuid():N}";
        request.Headers.TryAddWithoutValidation("X-Request-Id", requestId);
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", requestId);
        request.Headers.TryAddWithoutValidation("API-Version", ApiVersion);
        foreach (var (key, value) in RequestContext?.Headers() ?? new Dictionary<string, string>())
            request.Headers.TryAddWithoutValidation(key, value);

        foreach (var (key, value) in (IEnumerable<KeyValuePair<string, string>>?)headers ?? Array.Empty<KeyValuePair<string, string>>())
        {
            request.Headers.TryAddWithoutValidation(key, value);
        }

        return request;
    }

    private async Task<string> AccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(ApiKey)) return ApiKey;
        if (_oauthToken is not null && DateTimeOffset.UtcNow < _oauthExpiresAt.AddSeconds(-30)) return _oauthToken;
        await _oauthLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_oauthToken is not null && DateTimeOffset.UtcNow < _oauthExpiresAt.AddSeconds(-30)) return _oauthToken;
            var tokenUri = _oauth!.TokenUrl ?? new Uri(BaseUrl, "/oauth/token");
            using var request = new HttpRequestMessage(HttpMethod.Post, tokenUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_oauth.ClientId}:{_oauth.ClientSecret}")));
            var form = new Dictionary<string, string> { ["grant_type"] = "client_credentials" };
            if (_oauth.Scopes.Count > 0) form["scope"] = string.Join(' ', _oauth.Scopes);
            request.Content = new FormUrlEncodedContent(form);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) ThrowApiError(response, body);
            var payload = JsonNode.Parse(body)?.AsObject();
            _oauthToken = payload?["access_token"]?.GetValue<string>() ?? throw new WwsrapportAuthenticationException("OAuth server did not issue an access token.", response.StatusCode, payload, body, null);
            var expiresIn = payload?["expires_in"]?.GetValue<int>() ?? 300;
            _oauthExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn));
            return _oauthToken;
        }
        finally { _oauthLock.Release(); }
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
        var requestId = TryGetHeader(response, "X-WWS-Request-Id") ?? TryGetHeader(response, "X-Request-Id");
        var code = problem?["code"]?.ToString();

        throw response.StatusCode switch
        {
            HttpStatusCode.BadRequest when code is "invalid_input" or "validation_error" => new WwsrapportValidationException(message, response.StatusCode, problem, responseBody, requestId),
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
        _oauthLock.Dispose();
    }
}

public sealed record OAuthClientCredentialsOptions(string ClientId, string ClientSecret, IReadOnlyList<string>? RequestedScopes = null, Uri? TokenUrl = null)
{
    public IReadOnlyList<string> Scopes { get; } = RequestedScopes ?? Array.Empty<string>();
}

public sealed record PublicSectorRequestContext(string? MunicipalityCode = null, string? PurposeCode = null, string? CaseReference = null, string? ClientReference = null)
{
    internal IReadOnlyDictionary<string, string> Headers() => new Dictionary<string, string>
    {
        ["X-WWS-Municipality-Code"] = MunicipalityCode ?? "", ["X-WWS-Purpose-Code"] = PurposeCode ?? "",
        ["X-WWS-Case-Reference"] = CaseReference ?? "", ["X-WWS-Client-Reference"] = ClientReference ?? "",
    }.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)).ToDictionary();
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

    public Task<ApiEnvelope<ReportSummary>?> SubmitHumanReviewAsync(string reportId, object review, string idempotencyKey, CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<ApiEnvelope<ReportSummary>>(HttpMethod.Post, $"/reports/{Uri.EscapeDataString(reportId)}/human-review", review,
            headers: new Dictionary<string, string> { ["Idempotency-Key"] = idempotencyKey }, cancellationToken: cancellationToken);
}

public sealed class BatchesResource
{
    private readonly WwsrapportClient _client;
    internal BatchesResource(WwsrapportClient client) => _client = client;
    public Task<JsonObject?> CreateAsync(object input, string idempotencyKey, CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<JsonObject>(HttpMethod.Post, "/batches", input, headers: new Dictionary<string, string> { ["Idempotency-Key"] = idempotencyKey }, cancellationToken: cancellationToken);
    public Task<JsonObject?> GetAsync(string id, CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<JsonObject>(HttpMethod.Get, $"/batches/{Uri.EscapeDataString(id)}", cancellationToken: cancellationToken);
    public Task<JsonObject?> RetryAsync(string id, string idempotencyKey, CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<JsonObject>(HttpMethod.Post, $"/batches/{Uri.EscapeDataString(id)}/retry", headers: new Dictionary<string, string> { ["Idempotency-Key"] = idempotencyKey }, cancellationToken: cancellationToken);
}

public sealed class TenantLifecycleResource
{
    private readonly WwsrapportClient _client;
    internal TenantLifecycleResource(WwsrapportClient client) => _client = client;
    public Task<JsonObject?> RequestExportAsync(string idempotencyKey, CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<JsonObject>(HttpMethod.Post, "/exports", headers: new Dictionary<string, string> { ["Idempotency-Key"] = idempotencyKey }, cancellationToken: cancellationToken);
    public Task<JsonObject?> GetExportAsync(string id, CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<JsonObject>(HttpMethod.Get, $"/exports/{Uri.EscapeDataString(id)}", cancellationToken: cancellationToken);
    public Task<JsonObject?> CreateExportDownloadUrlAsync(string id, CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<JsonObject>(HttpMethod.Post, $"/exports/{Uri.EscapeDataString(id)}/download-url", cancellationToken: cancellationToken);
    public Task<JsonObject?> RequestOffboardingAsync(string reference, CancellationToken cancellationToken = default)
        => _client.RequestJsonAsync<JsonObject>(HttpMethod.Post, "/offboarding", new { confirmation = "REQUEST_OFFBOARDING", requested_by_reference = reference }, cancellationToken: cancellationToken);
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
