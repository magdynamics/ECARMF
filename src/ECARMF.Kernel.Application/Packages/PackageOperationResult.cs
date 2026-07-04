using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Packages;

public sealed record PackageOperationResult(
    bool Success,
    PackageLoadState? State,
    IReadOnlyList<string> Errors)
{
    public static PackageOperationResult Ok(PackageLoadState state) =>
        new(true, state, []);

    public static PackageOperationResult Fail(PackageLoadState? state, params string[] errors) =>
        new(false, state, errors);

    public static PackageOperationResult Fail(PackageLoadState? state, IReadOnlyList<string> errors) =>
        new(false, state, errors);
}
