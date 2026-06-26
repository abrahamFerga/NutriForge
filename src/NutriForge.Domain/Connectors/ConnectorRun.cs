namespace NutriForge.Domain.Connectors;

/// <summary>The outcome of the most recent run of an outbound connector.</summary>
public enum ConnectorRunStatus
{
    /// <summary>A run is in progress (recorded at the start, before it completes).</summary>
    Running,

    /// <summary>The run finished successfully.</summary>
    Success,

    /// <summary>The run threw and was abandoned.</summary>
    Failed,
}

/// <summary>
/// The latest run of an outbound connector (USDA, Open Food Facts, the LLM provider, …) — one row per
/// connector key, upserted on every run. It deliberately lives in the shared <c>appdb</c> so the
/// admin/registry endpoint (in the API process) can surface what the import worker (a separate
/// process) last did. Only batch connectors record runs; on-demand ones (a barcode lookup, a chat
/// turn) leave their last-run null.
/// </summary>
public sealed class ConnectorRun
{
    /// <summary>Stable connector identifier, e.g. <c>usda-fdc</c>. The primary key.</summary>
    public string ConnectorKey { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>When the run finished; null while a run is still in flight.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    public ConnectorRunStatus Status { get; set; }

    /// <summary>Rows imported/updated by the run (0 for non-import connectors).</summary>
    public int ItemsProcessed { get; set; }

    /// <summary>A short human-readable note — a summary on success, the error message on failure.</summary>
    public string? Detail { get; set; }
}
