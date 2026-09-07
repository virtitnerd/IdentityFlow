using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using IdentityFlow.Core.Abstractions;
using IdentityFlow.Core.Configuration;
using IdentityFlow.Core.Domain;

namespace IdentityFlow.Web.Pages.LifecyclePolicy;

public class IndexModel(ILifecycleTaskStore store) : PageModel
{
    public IReadOnlyList<LifecycleTask> Tasks { get; private set; } = [];
    public IReadOnlyList<LifecycleTaskExecution> RecentExecutions { get; private set; } = [];

    [BindProperty]
    public LifecycleTask Input { get; set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Tasks = await store.GetAllAsync(cancellationToken);
        RecentExecutions = await store.GetRecentExecutionsAsync(50, cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            Tasks = await store.GetAllAsync(cancellationToken);
            RecentExecutions = await store.GetRecentExecutionsAsync(50, cancellationToken);
            return Page();
        }

        await store.UpsertAsync(Input, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(id, cancellationToken);
        return RedirectToPage();
    }
}
