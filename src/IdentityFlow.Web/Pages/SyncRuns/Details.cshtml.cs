using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using IdentityFlow.Core.Abstractions;
using IdentityFlow.Core.Domain;

namespace IdentityFlow.Web.Pages.SyncRuns;

public class DetailsModel(ISyncRunStore syncRunStore) : PageModel
{
    public SyncRun? Run { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        Run = await syncRunStore.GetByIdAsync(id, cancellationToken);
        return Run is null ? NotFound() : Page();
    }
}
