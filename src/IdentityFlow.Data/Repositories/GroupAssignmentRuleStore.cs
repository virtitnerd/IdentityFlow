using Microsoft.EntityFrameworkCore;
using IdentityFlow.Core.Abstractions;
using IdentityFlow.Core.Configuration;

namespace IdentityFlow.Data.Repositories;

public sealed class GroupAssignmentRuleStore(ProvisionerDbContext db) : IGroupAssignmentRuleStore
{
    public async Task<IReadOnlyList<GroupAssignmentRule>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.GroupAssignmentRules.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);

    public async Task<GroupAssignmentRule> UpsertAsync(GroupAssignmentRule rule, CancellationToken cancellationToken = default)
    {
        db.GroupAssignmentRules.Upsert(rule, rule.Id);
        await db.SaveChangesAsync(cancellationToken);
        return rule;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await db.GroupAssignmentRules.Where(x => x.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}
