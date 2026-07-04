namespace ECARMF.Kernel.Domain.Packages;

/// <summary>
/// Outcome types are package-defined strings (approved, rejected, flagged,
/// hold, escalate, accept, improve, …), not a kernel enum. These constants
/// are the kernel's well-known conventions only: the default policy outcome
/// and the outcomes the dual-approval mechanism understands.
/// </summary>
public static class KernelOutcomes
{
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Flagged = "Flagged";
}
