using Microsoft.AspNetCore.Mvc.RazorPages;
using IdentityFlow.Core.Abstractions;
using IdentityFlow.Core.Domain;

namespace IdentityFlow.Web.Pages.SyncRuns;

public class IndexModel(ISyncRunStore syncRunStore) : PageModel
{
    public IReadOnlyList<SyncRun> Runs { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Runs = await syncRunStore.GetRecentAsync(200, cancellationToken);
    }
}
