# WWSrapport .NET client

Official .NET SDK for the WWSrapport API.

`client.Registry.DeriveBagReferenceAsync()`, `client.Registry.SearchByBagAsync()` and `client.Reports.VerificationAsync()` expose the Solana attestation flow. `WebhookEvents.All` contains all 27 supported event types.

## Official Links

- API overview and Swagger: [wwsrapport.nl/api/docs](https://wwsrapport.nl/api/docs)
- OpenAPI JSON: [wwsrapport.nl/api/openapi.json](https://wwsrapport.nl/api/openapi.json)
- Request a partner account: [wwsrapport.nl/api/toegang-aanvragen](https://wwsrapport.nl/api/toegang-aanvragen)
- WWSrapport account and API keys: [wwsrapport.nl/account](https://wwsrapport.nl/account)
- GitHub organization: [github.com/wwsrapport](https://github.com/wwsrapport)

Official clients:

- [PHP client](https://github.com/wwsrapport/php-client)
- [TypeScript client](https://github.com/wwsrapport/typescript-client)
- [Python client](https://github.com/wwsrapport/python-client)
- [.NET client](https://github.com/wwsrapport/dotnet-client)
- [API examples](https://github.com/wwsrapport/examples)

```bash
dotnet add package Wwsrapport.Client
```

Until the NuGet package is published, reference the project directly:

```xml
<ProjectReference Include="src/Wwsrapport.Client/Wwsrapport.Client.csproj" />
```

```csharp
using Wwsrapport;

var client = new WwsrapportClient(
    Environment.GetEnvironmentVariable("WWSRAPPORT_API_KEY")
        ?? throw new InvalidOperationException("Missing WWSRAPPORT_API_KEY")
);

var report = await client.Reports.CreateAsync(
    new ReportCreateInput
    {
        Address = new AddressInput
        {
            Postcode = "3905RB",
            HouseNumber = "4",
            City = "Veenendaal",
        },
        CustomerReference = "demo-001",
    },
    idempotencyKey: "demo-001"
);

Console.WriteLine(report.Data?.Id);
```

## Supported API resources

The SDK exposes typed resources for:

- property prefill;
- report validation, creation, listing and retrieval;
- report calculation and improvement advice data;
- PDF document listing and download;
- usage and rulesets;
- webhook endpoints, delivery attempts, test sends and retries.

The runtime response stays the exact JSON envelope returned by the API, while .NET gets safer autocomplete and nullable reference types.

## Download documents

```csharp
byte[] pdf = await client.Documents.DownloadWwsReportAsync("rpt_...");
await File.WriteAllBytesAsync("wwsrapport.pdf", pdf);

byte[] advicePdf = await client.Documents.DownloadImprovementAdviceAsync("rpt_...");
await File.WriteAllBytesAsync("wws-verbeteradvies.pdf", advicePdf);
```

## Webhooks

```csharp
using Wwsrapport.Webhooks;

string rawBody = await new StreamReader(Request.Body).ReadToEndAsync();
bool isValid = WebhookSignatureVerifier.Verify(
    rawBody,
    Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString()),
    Environment.GetEnvironmentVariable("WWSRAPPORT_WEBHOOK_SECRET")!
);

if (!isValid)
{
    return Results.Unauthorized();
}

var webhookEvent = WebhookSignatureVerifier.ParseEvent(rawBody);
Console.WriteLine(webhookEvent.Type);
```

The verifier expects:

- `WWS-Webhook-Timestamp`
- `WWS-Webhook-Signature`

The signature format is `v1=<hex-hmac-sha256>`, signed over:

```text
{timestamp}.{raw_body}
```

## Development

```bash
dotnet build src/Wwsrapport.Client/Wwsrapport.Client.csproj
dotnet run --project tests/Wwsrapport.Client.Tests/Wwsrapport.Client.Tests.csproj
```
