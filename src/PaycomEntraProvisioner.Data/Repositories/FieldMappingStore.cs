using Microsoft.EntityFrameworkCore;
using PaycomEntraProvisioner.Core.Abstractions;
using PaycomEntraProvisioner.Core.Configuration;

namespace PaycomEntraProvisioner.Data.Repositories;

public sealed class FieldMappingStore(ProvisionerDbContext db) : IFieldMappingStore
{
    public async Task<IReadOnlyList<FieldMapping>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.FieldMappings.AsNoTracking().OrderBy(x => x.SourceField).ToListAsync(cancellationToken);

    public async Task<FieldMapping> UpsertAsync(FieldMapping mapping, CancellationToken cancellationToken = default)
    {
        if (mapping.Id == 0)
        {
            db.FieldMappings.Add(mapping);
        }
        else
        {
            db.FieldMappings.Update(mapping);
        }

        await db.SaveChangesAsync(cancellationToken);
        return mapping;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await db.FieldMappings.Where(x => x.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}
