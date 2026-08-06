namespace Wwsrapport;

public static class WebhookEvents
{
    public static readonly IReadOnlyList<string> All = new[] { "report.queued","report.processing","report.calculation.ready","report.improvement-advice.ready","report.blockchain.finalized","report.completed","report.failed","report.issued","report.signature.completed","report.signature.failed","report.registry.submitted","report.registry.confirmed","report.registry.failed","report.superseded","report.cancelled","report.documents.ready","report.document.failed","monitor.created","monitor.updated","monitor.check_completed","monitor.event_created","monitor.event_resolved","impact_preview.created","listing_check.completed","report_job.updated","ruleset.changed","source.freshness_warning" };
}
