using IdentityFlow.Core.Domain;

namespace IdentityFlow.Core.Abstractions;

/// <summary>
/// Source of truth for worker data, from whichever HR system this
/// deployment is configured against. Paycom (<c>IdentityFlow.Clients.Paycom</c>)
/// is the only implementation today, but nothing above this interface -
/// <see cref="EmployeeRecord"/>, <see cref="IdentityFlow.Core.Sync.SyncOrchestrator"/>,
/// the rest of Core - knows or cares that it's Paycom specifically. Adding a
/// second HR system later means adding another <c>IdentityFlow.Clients.*</c>
/// implementation and registering it in place of (or alongside) Paycom's,
/// not touching anything in Core.
///
/// Paycom itself doesn't publish a single public API contract - customers
/// get SID/API-token or OAuth2 access provisioned by their Paycom
/// representative, and the exact report/field layout is configured per
/// tenant, which is why <c>PaycomHttpClient</c> normalizes whatever comes
/// back into the same canonical <see cref="EmployeeRecord"/> shape any
/// other implementation would also need to produce.
/// </summary>
public interface IHrClient
{
    Task<IReadOnlyList<EmployeeRecord>> GetAllEmployeesAsync(CancellationToken cancellationToken = default);
}
