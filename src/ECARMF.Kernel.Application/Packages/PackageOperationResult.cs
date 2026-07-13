using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Packages;

public sealed record PackageOperationResult(
    bool Success,
    PackageLoadState? State,
    IReadOnlyList<string> Errors)
{
    /// <summary>Non-blocking advisories surfaced alongside the outcome (TCEL
    /// P1.3): semantic-overlap nudges, consolidation tripwires, still-active
    /// superseded packages. Warnings NEVER change Success — an operation can
    /// succeed with warnings. Default empty keeps every existing caller and
    /// the serialized API shape backwards-compatible.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public static PackageOperationResult Ok(PackageLoadState state) =>
        new(true, state, []);

    public static PackageOperationResult Ok(PackageLoadState state, IReadOnlyList<string> warnings) =>
        new(true, state, []) { Warnings = warnings };

    public static PackageOperationResult Fail(PackageLoadState? state, params string[] errors) =>
        new(false, state, errors);

    public static PackageOperationResult Fail(PackageLoadState? state, IReadOnlyList<string> errors) =>
        new(false, state, errors);

    /// <summary>Returns a copy of this result carrying the given warnings.
    /// Used to attach advisories to an otherwise-unchanged outcome.</summary>
    public PackageOperationResult WithWarnings(IReadOnlyList<string> warnings) =>
        warnings.Count == 0 ? this : this with { Warnings = warnings };
}
