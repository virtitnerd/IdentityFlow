using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PaycomEntraProvisioner.Core.Abstractions;
using PaycomEntraProvisioner.Core.Domain;
using PaycomEntraProvisioner.Core.Sync;

namespace PaycomEntraProvisioner.Web.Pages;

public class IndexModel(SyncOrchestrator orchestrator, ISyncRunStore syncRunStore) : PageModel
{
    public IReadOnlyList<SyncRun> RecentRuns { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        RecentRuns = await syncRunStore.GetRecentAsync(10, cancellationToken);
    }

    public async Task<IActionResult> OnPostRunNowAsync(bool dryRun, CancellationToken cancellationToken)
    {
        var run = await orchestrator.RunAsync(SyncTrigger.Manual, User.Identity?.Name, dryRun, cancellationToken);
        TempData["LastRunId"] = run.Id.ToString();
        TempData["LastRunStatus"] = run.Status.ToString();
        return RedirectToPage("/SyncRuns/Details", new { id = run.Id });
    }
}
