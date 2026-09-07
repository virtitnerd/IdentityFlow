using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using IdentityFlow.Core.Abstractions;
using IdentityFlow.Core.Configuration;

namespace IdentityFlow.Web.Pages.GroupRules;

public class IndexModel(IGroupAssignmentRuleStore store) : PageModel
{
    public IReadOnlyList<GroupAssignmentRule> Rules { get; private set; } = [];

    [BindProperty]
    public GroupAssignmentRule Input { get; set; } = new() { Name = "", Condition = "", TargetGroupObjectId = "" };

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Rules = await store.GetAllAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            Rules = await store.GetAllAsync(cancellationToken);
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
