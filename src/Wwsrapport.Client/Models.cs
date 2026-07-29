using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Wwsrapport;

public sealed record ApiEnvelope<T>
{
    public T? Data { get; init; }
    public JsonObject? Meta { get; init; }
    public JsonObject? Links { get; init; }
}

public sealed record EmptyResponse
{
}

public sealed record AddressInput
{
    public required string Postcode { get; init; }
    public required string HouseNumber { get; init; }
    public string? HouseNumberAddition { get; init; }
    public string? Street { get; init; }
    public string? City { get; init; }
    public string? Country { get; init; }
}

public sealed record ReportCreateInput
{
    public required AddressInput Address { get; init; }
    public string? CustomerReference { get; init; }
    public string? CallbackUrl { get; init; }
    public JsonObject? Metadata { get; init; }
    public JsonObject? Input { get; init; }
}

public sealed record PropertyPrefill
{
    public AddressSnapshot? Address { get; init; }
    public string? BagId { get; init; }
    public string? EnergyLabel { get; init; }
    public decimal? LivingAreaM2 { get; init; }
    public decimal? WozValue { get; init; }
    public JsonObject? Sources { get; init; }
}

public sealed record AddressSnapshot
{
    public string? Postcode { get; init; }
    public string? HouseNumber { get; init; }
    public string? HouseNumberAddition { get; init; }
    public string? Street { get; init; }
    public string? City { get; init; }
    public string? Country { get; init; }
}

public sealed record ReportValidationResult
{
    public bool? Valid { get; init; }
    public List<ApiValidationError>? Errors { get; init; }
    public List<string>? Warnings { get; init; }
}

public sealed record ApiValidationError
{
    public string? Field { get; init; }
    public string? Message { get; init; }
    public string? Code { get; init; }
}

public sealed record ReportSummary
{
    public string? Id { get; init; }
    public string? PublicId { get; init; }
    public string? ReportNumber { get; init; }
    public string? ExternalReference { get; init; }
    public string? Status { get; init; }
    public string? StatusLabel { get; init; }
    public AddressSnapshot? Address { get; init; }
    public decimal? Points { get; init; }
    public decimal? MaxRentEur { get; init; }
    public string? RentSegment { get; init; }
    public string? RulesetVersion { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}

public sealed record CalculationCategory
{
    public string? Key { get; init; }
    public string? Label { get; init; }
    public decimal? Points { get; init; }
    public string? Status { get; init; }
    public JsonArray? Rules { get; init; }
}

public sealed record CalculationResult
{
    public string? ReportId { get; init; }
    public decimal? Points { get; init; }
    public decimal? MaxRentEur { get; init; }
    public string? RentSegment { get; init; }
    public string? RulesetVersion { get; init; }
    public List<CalculationCategory>? Categories { get; init; }
    public JsonArray? Assumptions { get; init; }
    public JsonArray? Warnings { get; init; }
}

public sealed record ImprovementOpportunity
{
    public string? Key { get; init; }
    public string? Title { get; init; }
    public string? Category { get; init; }
    public decimal? CurrentPoints { get; init; }
    public decimal? PotentialPoints { get; init; }
    public decimal? PointGain { get; init; }
    public decimal? RentImpactEur { get; init; }
}

public sealed record ImprovementAdvice
{
    public string? ReportId { get; init; }
    public string? Status { get; init; }
    public List<ImprovementOpportunity>? Opportunities { get; init; }
    public decimal? CurrentPoints { get; init; }
    public decimal? TargetPoints { get; init; }
    public decimal? PossiblePointGain { get; init; }
}

public sealed record DocumentSummary
{
    public string? Id { get; init; }
    public string? Type { get; init; }
    public string? Status { get; init; }
    public string? Filename { get; init; }
    public string? ContentType { get; init; }
    public long? SizeBytes { get; init; }
    public string? DownloadUrl { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
}

public sealed record UsageSummary
{
    public DateTimeOffset? PeriodStart { get; init; }
    public DateTimeOffset? PeriodEnd { get; init; }
    public int? ReportsIncluded { get; init; }
    public int? ReportsUsed { get; init; }
    public int? ReportsRemaining { get; init; }
    public bool? PaymentHold { get; init; }
}

public sealed record RulesetSummary
{
    public string? Id { get; init; }
    public string? Version { get; init; }
    public string? Label { get; init; }
    public DateOnly? ValidFrom { get; init; }
    public DateOnly? ValidUntil { get; init; }
    public string? Status { get; init; }
}

public sealed record WebhookCreateInput
{
    public required string Url { get; init; }
    public required IReadOnlyList<string> Events { get; init; }
    public string? Description { get; init; }
    public bool? Enabled { get; init; }
}

public sealed record WebhookUpdateInput
{
    public string? Url { get; init; }
    public IReadOnlyList<string>? Events { get; init; }
    public string? Description { get; init; }
    public bool? Enabled { get; init; }
}

public sealed record WebhookEndpoint
{
    public string? Id { get; init; }
    public string? Url { get; init; }
    public IReadOnlyList<string>? Events { get; init; }
    public string? Description { get; init; }
    public bool? Enabled { get; init; }
    public string? Status { get; init; }
    public string? SigningSecret { get; init; }
    public string? SecretHint { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed record WebhookDelivery
{
    public string? Id { get; init; }
    public string? WebhookId { get; init; }
    public string? EventType { get; init; }
    public string? Status { get; init; }
    public int? AttemptCount { get; init; }
    public int? ResponseStatus { get; init; }
    public DateTimeOffset? NextRetryAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
}

public sealed record WebhookEvent<T>
{
    public string? Id { get; init; }
    public required string Type { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public T? Data { get; init; }
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
