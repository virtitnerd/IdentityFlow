using IdentityFlow.Core.Domain;

namespace IdentityFlow.Core.Abstractions;

/// <summary>
/// Source of truth for worker data. Paycom does not publish a single public
/// API contract - customers get SID/API-token or OAuth2 access provisioned
/// by their Paycom representative, and the exact report/field layout is
/// configured per tenant. Implementations should normalize whatever comes
/// back (REST JSON report, CSV export, etc.) into <see cref="EmployeeRecord"/>.
/// </summary>
public interface IPaycomClient
{
    Task<IReadOnlyList<EmployeeRecord>> GetAllEmployeesAsync(CancellationToken cancellationToken = default);
}
