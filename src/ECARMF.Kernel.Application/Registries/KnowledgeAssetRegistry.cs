using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Registries;

public interface IKnowledgeAssetRegistry : IRegistry<KnowledgeAsset>
{
    /// <summary>Effective-dated retrieval: only versions whose
    /// [EffectiveFrom, EffectiveTo] range covers <paramref name="asOf"/> —
    /// a 2027 evaluation never sees 2025 rules.</summary>
    IReadOnlyList<Registered<KnowledgeAsset>> GetEffective(
        DateTimeOffset asOf, string? docType = null, string? docKey = null, string? assetType = null);
}

/// <summary>Catalog of retrievable knowledge assets contributed by active
/// Knowledge Packages — the ninth kernel registry (Batch 2, Refinement 8;
/// supersedes the MagCPA reference library with the same mechanism).</summary>
public class KnowledgeAssetRegistry : RegistryBase<KnowledgeAsset>, IKnowledgeAssetRegistry
{
    protected override string GetName(KnowledgeAsset declaration) => declaration.AssetId;

    public IReadOnlyList<Registered<KnowledgeAsset>> GetEffective(
        DateTimeOffset asOf, string? docType = null, string? docKey = null, string? assetType = null) =>
        Where(r =>
            r.Declaration.EffectiveFrom <= asOf
            && (r.Declaration.EffectiveTo is not { } to || to >= asOf)
            && (string.IsNullOrWhiteSpace(docType)
                || string.Equals(r.Declaration.DocType, docType, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(docKey)
                || string.Equals(r.Declaration.DocKey, docKey, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(assetType)
                || string.Equals(r.Declaration.AssetType, assetType, StringComparison.OrdinalIgnoreCase)));
}
