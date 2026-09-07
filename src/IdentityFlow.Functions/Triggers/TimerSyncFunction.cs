using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using IdentityFlow.Core.Domain;
using IdentityFlow.Core.Sync;

namespace IdentityFlow.Functions.Triggers;

/// <summary>
/// Scheduled entry point for the Paycom -> Entra ID sync. The schedule is
/// read from the "SyncSchedule" app setting (NCRONTAB, 6 fields including
/// seconds) so it can be changed per environment without a redeploy.
/// </summary>
public sealed class TimerSyncFunction(SyncOrchestrator orchestrator, ILogger<TimerSyncFunction> logger)
{
    [Function("TimerSync")]
    public async Task RunAsync([TimerTrigger("%SyncSchedule%")] TimerInfo timer, CancellationToken cancellationToken)
    {
        logger.LogInformation("Timer-triggered sync starting (next scheduled run: {Next}).", timer.ScheduleStatus?.Next);

        var run = await orchestrator.RunAsync(SyncTrigger.Timer, triggeredByUser: null, dryRun: false, cancellationToken);

        logger.LogInformation(
            "Timer-triggered sync {RunId} completed with status {Status}: {Submitted} submitted, {Failed} failed, {Skipped} skipped.",
            run.Id, run.Status, run.RecordsSubmitted, run.RecordsFailed, run.RecordsSkipped);
    }
}
