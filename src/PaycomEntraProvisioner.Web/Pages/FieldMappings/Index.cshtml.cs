using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PaycomEntraProvisioner.Core.Abstractions;
using PaycomEntraProvisioner.Core.Configuration;
using PaycomEntraProvisioner.Core.Mapping;

namespace PaycomEntraProvisioner.Web.Pages.FieldMappings;

public class IndexModel(
    IFieldMappingStore store,
    IDiscoveredFieldStore discoveredFieldStore,
    IEntraDirectoryClient directoryClient) : PageModel
{
    public IReadOnlyList<FieldMapping> Mappings { get; private set; } = [];

    /// <summary>
    /// Paycom field names actually seen in past sync runs (including dry
    /// runs) - suggestions only, not a restriction; see the note on the
    /// page itself.
    /// </summary>
    public IReadOnlyList<string> KnownPaycomFields { get; private set; } = [];

    /// <summary>
    /// Standard SCIM attributes + extensionAttribute1-15 + any custom
    /// directory extension attributes registered on the sync-service app
    /// registration, merged into one suggestion list for the target field.
    /// </summary>
    public IReadOnlyList<string> EntraAttributeSuggestions { get; private set; } = [];

    [BindProperty]
    public FieldMapping Input { get; set; } = new() { SourceField = "", TargetAttribute = "" };

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Mappings = await store.GetAllAsync(cancellationToken);
        await LoadSuggestionsAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            Mappings = await store.GetAllAsync(cancellationToken);
            await LoadSuggestionsAsync(cancellationToken);
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

    private async Task LoadSuggestionsAsync(CancellationToken cancellationToken)
    {
        KnownPaycomFields = await discoveredFieldStore.GetKnownFieldNamesAsync(cancellationToken);

        // Custom directory extensions require a live Graph call; never let
        // that failure block loading this page - fall back to the
        // standard/extensionAttribute lists alone.
        IReadOnlyList<string> customExtensions;
        try
        {
            customExtensions = await directoryClient.GetCustomExtensionAttributeNamesAsync(cancellationToken);
        }
        catch
        {
            customExtensions = [];
        }

        EntraAttributeSuggestions = [
            .. KnownEntraAttributes.StandardAttributes,
            .. KnownEntraAttributes.ExtensionAttributeSlots,
            .. customExtensions
        ];
    }
}
