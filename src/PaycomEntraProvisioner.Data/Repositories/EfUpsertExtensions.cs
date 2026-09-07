using Microsoft.EntityFrameworkCore;

namespace PaycomEntraProvisioner.Data.Repositories;

/// <summary>
/// The "Id == 0 means new, else existing" upsert pattern used identically
/// by every simple admin-config store (field mappings, group rules) - kept
/// in one place instead of hand-rolled per repository, where a fix to one
/// could silently fail to reach the others.
/// </summary>
internal static class EfUpsertExtensions
{
    public static void Upsert<TEntity>(this DbSet<TEntity> set, TEntity entity, int id) where TEntity : class
    {
        if (id == 0)
        {
            set.Add(entity);
        }
        else
        {
            set.Update(entity);
        }
    }
}
