using System.Diagnostics;

namespace PoTraffic.API.Infrastructure.Observability;

/// <summary>
/// The app's own spans: one <see cref="ActivitySource"/> for every background job, paid
/// provider call and Table Storage write, so a slow poll reads as one trace — job → Google
/// Maps → weather → storage — instead of disconnected HTTP dependencies.
///
/// <para>The source is registered with the tracer provider only when an exporter is
/// configured (<see cref="ObservabilityExtensions"/>). Without one nothing listens, so
/// <see cref="ActivitySource.StartActivity(string, ActivityKind)"/> returns null and every
/// call site below costs a null check.</para>
///
/// <para>Tags carry ids, counts and provider status codes only — never addresses,
/// coordinates or emails. Anything user-identifying goes through <c>PiiRedactor</c> in logs
/// and stays out of spans entirely.</para>
/// </summary>
public static class PoTrafficTelemetry
{
    public const string SourceName = "PoTraffic.API";

    public static readonly ActivitySource Source = new(SourceName);

    /// <summary>Prefix of every background-job span name; <see cref="CompositeRoutingSampler"/>
    /// budgets these separately from request traces.</summary>
    public const string JobSpanPrefix = "Job.";

    // One spelling per tag across every span.
    public const string JobIdTag = "job.id";
    public const string JobTypeTag = "job.type";
    public const string JobMethodTag = "job.method";
    public const string JobKindTag = "job.kind";
    public const string RouteIdTag = "route.id";
    public const string PollOutcomeTag = "poll.outcome";
    public const string ProviderTag = "provider.name";
    public const string ProviderOperationTag = "provider.operation";
    public const string ProviderStatusTag = "provider.status";
    public const string StorageUpsertsTag = "storage.upserts";
    public const string StorageDeletesTag = "storage.deletes";
    public const string StorageTablesTag = "storage.tables";

    /// <summary>A client span around one third-party call; null when no exporter listens.</summary>
    public static Activity? StartProviderCall(string provider, string operation)
    {
        Activity? activity = Source.StartActivity($"{provider}.{operation}", ActivityKind.Client);
        activity?.SetTag(ProviderTag, provider);
        activity?.SetTag(ProviderOperationTag, operation);
        return activity;
    }

    /// <summary>
    /// Marks the span failed with the exception's type only. The message is left out on
    /// purpose: provider and storage exceptions can quote the request URL or row payload, and
    /// the log line written alongside already carries the full, redacted detail.
    /// </summary>
    public static void RecordFailure(this Activity? activity, Exception ex)
    {
        if (activity is null)
            return;
        activity.SetTag("exception.type", ex.GetType().FullName);
        activity.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
    }
}
