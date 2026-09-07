namespace IdentityFlow.Core.Domain;

/// <summary>
/// Named values for <see cref="SyncRunEmployeeResult.Outcome"/>, kept in
/// one place rather than as string literals scattered across
/// SyncOrchestrator and the store that queries by them - the same
/// drift risk fixed elsewhere in this codebase (see MappingEngine's
/// attribute-handler table) applies here: the orchestrator writes these
/// strings and the store filters by them, so they must agree exactly.
/// </summary>
public static class SyncOutcomes
{
    public const string Skipped = "Skipped";
    public const string DryRunPreview = "DryRunPreview";
    public const string MappingError = "MappingError";

    /// <summary>
    /// Sent to Entra's bulkUpload endpoint and accepted at the HTTP level -
    /// not yet confirmed via the provisioning audit log. The synchronous
    /// bulkUpload response isn't a reliable per-record result (Microsoft's
    /// own reference implementation doesn't trust it either), so this is an
    /// interim state a later run's reconciliation pass resolves to
    /// <see cref="Provisioned"/> or <see cref="SubmissionError"/>.
    /// </summary>
    public const string Submitted = "Submitted";

    /// <summary>Confirmed by the provisioning audit log as successfully created/updated/disabled.</summary>
    public const string Provisioned = "Provisioned";

    /// <summary>The provisioning job decided no change was needed - not a failure.</summary>
    public const string ProvisioningSkipped = "ProvisioningSkipped";

    public const string SubmissionError = "SubmissionError";
    public const string VanishedFromFeedAutoDisabled = "VanishedFromFeed-AutoDisabled";
    public const string GroupRuleError = "GroupRuleError";
    public const string DryRunGroupPreview = "DryRunGroupPreview";
    public const string GroupReconciliationError = "GroupReconciliationError";

    /// <summary>A one-time <see cref="LifecycleTaskType"/> ran successfully for an employee.</summary>
    public const string LifecycleTaskCompleted = "LifecycleTaskCompleted";

    public const string LifecycleTaskError = "LifecycleTaskError";
    public const string DryRunLifecyclePreview = "DryRunLifecyclePreview";
}
