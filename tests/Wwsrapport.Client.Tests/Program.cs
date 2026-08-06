using System.Net;
using System.Text;
using Wwsrapport;
using Wwsrapport.Webhooks;

await RunsCreateReportWithIdempotencyKey();
await MapsApiErrors();
VerifiesWebhookSignatures();

Console.WriteLine("All tests passed.");

static async Task RunsCreateReportWithIdempotencyKey()
{
    var handler = new FakeHandler(request =>
    {
        AssertEqual(HttpMethod.Post, request.Method, "method");
        AssertEqual("/v1/reports", request.RequestUri?.AbsolutePath, "path");
        AssertEqual("Bearer", request.Headers.Authorization?.Scheme, "auth scheme");
        AssertEqual("test-key", request.Headers.Authorization?.Parameter, "auth token");
        AssertEqual("idem-1", request.Headers.GetValues("Idempotency-Key").Single(), "idempotency key");
        AssertEqual("wwsrapport-dotnet/0.2.0", request.Headers.GetValues("X-WWSrapport-Client").Single(), "client header");

        return Json(HttpStatusCode.OK, "{\"data\":{\"id\":\"rpt_123\",\"status\":\"draft\",\"address\":{\"postcode\":\"3905RB\",\"house_number\":\"4\"}}}");
    });

    using var client = new WwsrapportClient("test-key", "https://wwsrapport.nl/v1", new HttpClient(handler));
    var response = await client.Reports.CreateAsync(
        new ReportCreateInput
        {
            Address = new AddressInput
            {
                Postcode = "3905RB",
                HouseNumber = "4",
            },
        },
        "idem-1"
    );

    AssertEqual("rpt_123", response?.Data?.Id, "report id");
    AssertEqual("draft", response?.Data?.Status, "report status");
}

static async Task MapsApiErrors()
{
    var handler = new FakeHandler(_ => Json(HttpStatusCode.TooManyRequests, "{\"detail\":\"Too many requests\"}", "req_123"));
    using var client = new WwsrapportClient("test-key", "https://wwsrapport.nl/v1", new HttpClient(handler));

    try
    {
        await client.Usage.CurrentAsync();
    }
    catch (WwsrapportRateLimitException exception)
    {
        AssertEqual(HttpStatusCode.TooManyRequests, exception.StatusCode, "status code");
        AssertEqual("Too many requests", exception.Message, "error message");
        AssertEqual("req_123", exception.RequestId, "request id");
        return;
    }

    throw new Exception("Expected WwsrapportRateLimitException.");
}

static void VerifiesWebhookSignatures()
{
    const string payload = "{\"id\":\"evt_1\",\"type\":\"report.completed\",\"data\":{\"id\":\"rpt_123\"}}";
    const string timestamp = "1780000000";
    const string secret = "whsec_test";
    var signature = WebhookSignatureVerifier.ComputeSignature(timestamp, payload, secret);
    var headers = new Dictionary<string, string>
    {
        ["WWS-Webhook-Timestamp"] = timestamp,
        ["WWS-Webhook-Signature"] = $"v1={signature}",
    };

    var ok = WebhookSignatureVerifier.Verify(payload, headers, secret, now: DateTimeOffset.FromUnixTimeSeconds(1780000010));
    AssertEqual(true, ok, "valid signature");

    var webhookEvent = WebhookSignatureVerifier.ParseEvent(payload);
    AssertEqual("report.completed", webhookEvent?.Type, "event type");
}

static HttpResponseMessage Json(HttpStatusCode statusCode, string body, string? requestId = null)
{
    var response = new HttpResponseMessage(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    if (requestId is not null)
    {
        response.Headers.TryAddWithoutValidation("X-Request-Id", requestId);
    }

    return response;
}

static void AssertEqual(object? expected, object? actual, string label)
{
    if (!Equals(expected, actual))
    {
        throw new Exception($"{label}: expected {expected}, got {actual}.");
    }
}

sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(_handler(request));
}
