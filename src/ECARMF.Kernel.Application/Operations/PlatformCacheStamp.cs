namespace ECARMF.Kernel.Application.Operations;

/// <summary>
/// Version stamp for the platform-wide roll-up caches (catalog, skills
/// library, platform risk). Cached values embed the version in their key, so
/// any mutation that changes the package landscape bumps the version and the
/// next read recomputes — no fragile per-key eviction lists.
/// </summary>
public interface IPlatformCacheStamp
{
    long Version { get; }
    void Invalidate();
}

public class PlatformCacheStamp : IPlatformCacheStamp
{
    private long _version;
    public long Version => Interlocked.Read(ref _version);
    public void Invalidate() => Interlocked.Increment(ref _version);
}
