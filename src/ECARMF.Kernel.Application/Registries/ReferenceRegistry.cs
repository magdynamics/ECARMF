using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Registries;

public interface IReferenceRegistry : IRegistry<ReferenceDocumentDeclaration>
{
    /// <summary>Effective-dated retrieval: only versions whose
    /// [EffectiveFrom, EffectiveTo] range covers <paramref name="asOf"/> —
    /// a 2027 evaluation never sees 2025 rules.</summary>
    IReadOnlyList<Registered<ReferenceDocumentDeclaration>> GetEffective(
        DateTimeOffset asOf, string? docType = null, string? docKey = null);
}

/// <summary>Catalog of retrievable regulatory/professional references
/// contributed by active Knowledge Packages — the ninth kernel registry
/// (MagCPA Requirement 5).</summary>
public class ReferenceRegistry : RegistryBase<ReferenceDocumentDeclaration>, IReferenceRegistry
{
    protected override string GetName(ReferenceDocumentDeclaration declaration) => declaration.ReferenceId;

    public IReadOnlyList<Registered<ReferenceDocumentDeclaration>> GetEffective(
        DateTimeOffset asOf, string? docType = null, string? docKey = null) =>
        Where(r =>
            r.Declaration.EffectiveFrom <= asOf
            && (r.Declaration.EffectiveTo is not { } to || to >= asOf)
            && (string.IsNullOrWhiteSpace(docType)
                || string.Equals(r.Declaration.DocType, docType, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(docKey)
                || string.Equals(r.Declaration.DocKey, docKey, StringComparison.OrdinalIgnoreCase)));
}
