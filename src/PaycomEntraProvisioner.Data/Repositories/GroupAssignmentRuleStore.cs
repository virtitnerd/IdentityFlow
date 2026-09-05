using Microsoft.EntityFrameworkCore;
using PaycomEntraProvisioner.Core.Abstractions;
using PaycomEntraProvisioner.Core.Configuration;

namespace PaycomEntraProvisioner.Data.Repositories;

public sealed class GroupAssignmentRuleStore(ProvisionerDbContext db) : IGroupAssignmentRuleStore
{
    public async Task<IReadOnlyList<GroupAssignmentRule>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.GroupAssignmentRules.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);

    public async Task<GroupAssignmentRule> UpsertAsync(GroupAssignmentRule rule, CancellationToken cancellationToken = default)
    {
        if (rule.Id == 0)
        {
            db.GroupAssignmentRules.Add(rule);
        }
        else
        {
            db.GroupAssignmentRules.Update(rule);
        }

        await db.SaveChangesAsync(cancellationToken);
        return rule;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await db.GroupAssignmentRules.Where(x => x.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}
