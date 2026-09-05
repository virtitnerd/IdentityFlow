using Microsoft.EntityFrameworkCore;
using PaycomEntraProvisioner.Core.Abstractions;
using PaycomEntraProvisioner.Core.Domain;

namespace PaycomEntraProvisioner.Data.Repositories;

public sealed class DiscoveredFieldStore(ProvisionerDbContext db) : IDiscoveredFieldStore
{
    public async Task<IReadOnlyList<string>> GetKnownFieldNamesAsync(CancellationToken cancellationToken = default) =>
        await db.DiscoveredSourceFields
            .AsNoTracking()
            .OrderBy(x => x.FieldName)
            .Select(x => x.FieldName)
            .ToListAsync(cancellationToken);

    public async Task RecordObservedFieldsAsync(
        IEnumerable<string> fieldNames,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        var names = fieldNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count == 0)
        {
            return;
        }

        var existing = await db.DiscoveredSourceFields
            .Where(x => names.Contains(x.FieldName))
            .ToDictionaryAsync(x => x.FieldName, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var name in names)
        {
            if (existing.TryGetValue(name, out var field))
            {
                field.LastSeenAt = observedAt;
            }
            else
            {
                db.DiscoveredSourceFields.Add(new DiscoveredSourceField
                {
                    FieldName = name,
                    FirstSeenAt = observedAt,
                    LastSeenAt = observedAt
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
