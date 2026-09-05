using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PaycomEntraProvisioner.Core.Abstractions;
using PaycomEntraProvisioner.Core.Configuration;

namespace PaycomEntraProvisioner.Web.Pages.FieldMappings;

public class IndexModel(IFieldMappingStore store) : PageModel
{
    public IReadOnlyList<FieldMapping> Mappings { get; private set; } = [];

    [BindProperty]
    public FieldMapping Input { get; set; } = new() { SourceField = "", TargetAttribute = "" };

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Mappings = await store.GetAllAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            Mappings = await store.GetAllAsync(cancellationToken);
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
