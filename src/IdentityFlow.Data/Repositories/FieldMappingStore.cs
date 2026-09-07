using Microsoft.EntityFrameworkCore;
using IdentityFlow.Core.Abstractions;
using IdentityFlow.Core.Configuration;

namespace IdentityFlow.Data.Repositories;

public sealed class FieldMappingStore(ProvisionerDbContext db) : IFieldMappingStore
{
    public async Task<IReadOnlyList<FieldMapping>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.FieldMappings.AsNoTracking().OrderBy(x => x.SourceField).ToListAsync(cancellationToken);

    public async Task<FieldMapping> UpsertAsync(FieldMapping mapping, CancellationToken cancellationToken = default)
    {
        db.FieldMappings.Upsert(mapping, mapping.Id);
        await db.SaveChangesAsync(cancellationToken);
        return mapping;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await db.FieldMappings.Where(x => x.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}
