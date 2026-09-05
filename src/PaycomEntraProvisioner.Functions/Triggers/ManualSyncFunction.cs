using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PaycomEntraProvisioner.Core.Abstractions;
using PaycomEntraProvisioner.Core.Domain;
using PaycomEntraProvisioner.Core.Sync;

namespace PaycomEntraProvisioner.Functions.Triggers;

/// <summary>
/// On-demand sync trigger, secured with a function key. Intended for
/// ops/automation use (a runbook, a manual curl, a Logic App) - the Razor
/// admin UI's "Run now" button calls <see cref="SyncOrchestrator"/>
/// in-process instead, since it's deployed with the same DI graph and
/// doesn't need a network hop or a second credential.
/// </summary>
public sealed class ManualSyncFunction(SyncOrchestrator orchestrator, ISyncRunStore syncRunStore, ILogger<ManualSyncFunction> logger)
{
    [Function("ManualSync")]
    public async Task<IActionResult> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "sync")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        var dryRun = req.Query.TryGetValue("dryRun", out var dryRunValue) && bool.TryParse(dryRunValue, out var parsed) && parsed;

        logger.LogInformation("Manual sync triggered via HTTP (dryRun={DryRun}).", dryRun);

        var run = await orchestrator.RunAsync(SyncTrigger.Api, triggeredByUser: "http-trigger", dryRun, cancellationToken);

        return new OkObjectResult(new
        {
            run.Id,
            run.Status,
            run.EmployeesEvaluated,
            run.RecordsSubmitted,
            run.RecordsFailed,
            run.RecordsSkipped,
            run.GroupMembershipsAdded,
            run.GroupMembershipsRemoved,
            run.ErrorSummary
        });
    }

    [Function("RecentSyncRuns")]
    public async Task<IActionResult> GetRecentAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "sync/recent")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        var runs = await syncRunStore.GetRecentAsync(25, cancellationToken);
        return new OkObjectResult(runs);
    }
}
