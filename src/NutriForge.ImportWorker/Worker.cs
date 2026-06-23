namespace NutriForge.ImportWorker;

// Placeholder host for the nightly USDA FoodData Central / Open Food Facts import
// pipeline (ROADMAP Phase 0). For now it just stays alive so the AppHost has a
// second compute unit to compose and the deploy path (the Container App Job) is
// exercised. Phase 0 replaces ExecuteAsync with the real import services
// (fetch -> normalize to per-100g vectors -> upsert by (provider, providerId)).
public sealed partial class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(logger);
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown — expected when the host stops.
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Import worker started; awaiting the nightly schedule.")]
    private static partial void LogStarted(ILogger logger);
}
