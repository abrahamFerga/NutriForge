using Microsoft.Extensions.DependencyInjection;
using NutriForge.Infrastructure.UsdaFdc;

namespace NutriForge.ImportWorker;

/// <summary>
/// The nightly catalog import worker. When <c>Usda:DataDirectory</c> points at a folder of USDA
/// FoodData Central CSVs it runs the idempotent import on startup (re-running upserts, never
/// duplicates — the Container App Job's cron provides the nightly cadence). With no directory
/// configured it idles, so local/dev runs and the integration host are unaffected.
/// </summary>
public sealed partial class Worker(
    IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(logger);

        var dir = config["Usda:DataDirectory"];
        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
        {
            try
            {
                await RunImportAsync(dir, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // a failed import must not crash the worker
            catch (Exception ex)
            {
                LogImportFailed(logger, dir, ex);
            }
#pragma warning restore CA1031
        }
        else
        {
            LogNoData(logger);
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown — expected when the host stops.
        }
    }

    private async Task RunImportAsync(string directory, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var importer = scope.ServiceProvider.GetRequiredService<UsdaFdcImporter>();
        var foods = await new UsdaFdcCsvSource(directory).LoadAsync(ct).ConfigureAwait(false);
        var result = await importer.ImportAsync(foods, ct).ConfigureAwait(false);
        LogImported(logger, result.Inserted, result.Updated, foods.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Import worker started.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "No Usda:DataDirectory configured; import worker idle.")]
    private static partial void LogNoData(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "USDA import complete: {Inserted} inserted, {Updated} updated ({Total} foods).")]
    private static partial void LogImported(ILogger logger, int inserted, int updated, int total);

    [LoggerMessage(Level = LogLevel.Error, Message = "USDA import from {Directory} failed.")]
    private static partial void LogImportFailed(ILogger logger, string directory, Exception ex);
}
