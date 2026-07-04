using System.Diagnostics.CodeAnalysis;

namespace ECARMF.Kernel.Application.Registries;

/// <summary>
/// Thread-safe name-keyed registry. Names are case-insensitive; the API and
/// the event processing engine read concurrently while the package loader
/// mutates, so all access is serialized on a single lock.
/// </summary>
public abstract class RegistryBase<TDeclaration> : IRegistry<TDeclaration>
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Registered<TDeclaration>> _items =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Extracts the unique registry name from a declaration.</summary>
    protected abstract string GetName(TDeclaration declaration);

    public void Register(TDeclaration declaration, string packageId, string packageVersion)
    {
        var name = GetName(declaration);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                $"A {typeof(TDeclaration).Name} must declare a non-empty name.", nameof(declaration));
        }

        lock (_sync)
        {
            if (_items.TryGetValue(name, out var existing))
            {
                throw new RegistryConflictException(name, existing.PackageId, existing.PackageVersion);
            }

            _items[name] = new Registered<TDeclaration>(
                declaration, packageId, packageVersion, DateTimeOffset.UtcNow);
        }
    }

    public void UnregisterPackage(string packageId, string packageVersion)
    {
        lock (_sync)
        {
            var owned = _items
                .Where(kv => string.Equals(kv.Value.PackageId, packageId, StringComparison.OrdinalIgnoreCase)
                          && string.Equals(kv.Value.PackageVersion, packageVersion, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in owned)
            {
                _items.Remove(key);
            }
        }
    }

    public bool TryGet(string name, [NotNullWhen(true)] out Registered<TDeclaration>? registration)
    {
        lock (_sync)
        {
            if (_items.TryGetValue(name, out var found))
            {
                registration = found;
                return true;
            }
        }

        registration = null;
        return false;
    }

    public bool Contains(string name)
    {
        lock (_sync)
        {
            return _items.ContainsKey(name);
        }
    }

    public IReadOnlyList<Registered<TDeclaration>> GetAll()
    {
        lock (_sync)
        {
            return _items.Values.ToList();
        }
    }

    /// <summary>Snapshot filtered under the lock, for registry-specific queries.</summary>
    protected IReadOnlyList<Registered<TDeclaration>> Where(Func<Registered<TDeclaration>, bool> predicate)
    {
        lock (_sync)
        {
            return _items.Values.Where(predicate).ToList();
        }
    }
}
