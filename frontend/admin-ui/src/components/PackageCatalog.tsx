import { useEffect, useMemo, useState } from 'react'
import { api, ApiError } from '../api'

// Platform Package Library (operator). Packages are authored per tenant, but
// any manifest loaded anywhere is an installable unit — so this is the union
// across all tenants, and installing copies a package (with its dependencies)
// into a target tenant. This is what makes T9-041/T9-042 (and every other
// package) a platform-level offering rather than tenant-locked.

interface CatalogEntry {
  packageId: string
  packageVersion: string
  name: string
  publisher: string
  description?: string | null
  dependencies: string[]
  entities: number
  controls: number
  events: number
  agents: number
  kpis: number
  knowledgeAssets: number
  installedInTenants: string[]
}

interface InstallResult { activated: string[]; skipped: string[]; errors: string[] }

export function PackageCatalog({ tenant }: { tenant: string; user: string }) {
  const [entries, setEntries] = useState<CatalogEntry[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [filter, setFilter] = useState('')
  const [selected, setSelected] = useState<CatalogEntry | null>(null)
  const [tenants, setTenants] = useState<string[]>([])
  const [target, setTarget] = useState('')
  const [withDeps, setWithDeps] = useState(true)
  const [installing, setInstalling] = useState(false)
  const [result, setResult] = useState<InstallResult | null>(null)
  const [installError, setInstallError] = useState<string | null>(null)

  useEffect(() => {
    api.get<CatalogEntry[]>('/api/catalog').then(setEntries).catch((e) => {
      setError(e instanceof ApiError ? e.message : String(e)); setEntries([])
    })
    api.get<{ tenantId: string }[]>('/api/platform/tenants')
      .then((list) => setTenants(list.map((t) => t.tenantId)))
      .catch(() => {})
  }, [])

  // Default the install target to the tenant currently being viewed (unless
  // that's the operator tenant, which holds no business data).
  useEffect(() => {
    if (!target && tenant && tenant.toLowerCase() !== 'platform') setTarget(tenant)
  }, [tenant, target])

  function pick(e: CatalogEntry) {
    setSelected(e); setResult(null); setInstallError(null)
  }

  async function install() {
    if (!selected || !target) return
    setInstalling(true); setResult(null); setInstallError(null)
    try {
      const r = await api.post<InstallResult>(`/api/platform/tenants/${target}/catalog/install`, {
        packageId: selected.packageId, version: selected.packageVersion, withDependencies: withDeps,
      })
      setResult(r)
    } catch (e) {
      setInstallError(e instanceof ApiError ? e.message : String(e))
    } finally {
      setInstalling(false)
    }
  }

  const f = filter.trim().toLowerCase()
  const list = useMemo(() => (entries ?? []).filter((e) =>
    !f || e.name.toLowerCase().includes(f) || e.packageId.toLowerCase().includes(f)
    || (e.description ?? '').toLowerCase().includes(f)), [entries, f])

  const alreadyThere = selected != null && target !== '' && selected.installedInTenants.some(
    (t) => t.toLowerCase() === target.toLowerCase())

  return (
    <div>
      <section className="panel">
        <h2>Package Library</h2>
        <p className="muted">
          Every knowledge package available on the platform — the controls, entities, agents and KPIs
          a client can be given. Pick one and install it into any tenant; dependencies come along.
          Nothing is auto-provisioned, so this is where you add capabilities like T9-041/T9-042 to a client.
        </p>
        <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center', flexWrap: 'wrap' }}>
          <input placeholder="Search packages…" value={filter} onChange={(e) => setFilter(e.target.value)} style={{ minWidth: '18rem' }} />
          <span className="muted small">{list.length} of {entries?.length ?? 0} package(s)</span>
        </div>
        {error && <p className="error small">{error}</p>}
      </section>

      <div className="agents-layout">
        <section className="panel agents-list">
          {entries === null ? (
            <p className="muted">Loading catalog…</p>
          ) : list.length === 0 ? (
            <p className="muted">No packages{f ? ' match your filter' : ' loaded anywhere yet'}.</p>
          ) : (
            list.map((e) => (
              <button
                key={`${e.packageId}@${e.packageVersion}`}
                className={`agent-card ${selected?.packageId === e.packageId && selected?.packageVersion === e.packageVersion ? 'active' : ''}`}
                onClick={() => pick(e)}
              >
                <div className="agent-card-head">
                  <strong>{e.name}</strong>
                  <span className="mono small">v{e.packageVersion}</span>
                </div>
                <span className="muted small">
                  {e.controls}C · {e.entities}E · {e.agents}A · {e.kpis}K · {e.knowledgeAssets} docs
                </span>
                <span className="mono small" style={{ opacity: 0.7 }}>
                  {e.installedInTenants.length > 0 ? `in ${e.installedInTenants.length} tenant(s)` : 'not active anywhere'}
                </span>
              </button>
            ))
          )}
        </section>

        <section className="panel agents-detail">
          {!selected ? (
            <p className="muted">Select a package to see what it contributes and install it into a tenant.</p>
          ) : (
            <>
              <h3>{selected.name} <span className="mono small">v{selected.packageVersion}</span></h3>
              <p className="muted small">{selected.packageId} · {selected.publisher}</p>
              {selected.description && <p className="small">{selected.description}</p>}

              <div className="agent-meta">
                <span><span className="muted small">Controls</span> {selected.controls}</span>
                <span><span className="muted small">Entities</span> {selected.entities}</span>
                <span><span className="muted small">Events</span> {selected.events}</span>
                <span><span className="muted small">Agents</span> {selected.agents}</span>
                <span><span className="muted small">KPIs</span> {selected.kpis}</span>
                <span><span className="muted small">Knowledge</span> {selected.knowledgeAssets}</span>
              </div>

              {selected.dependencies.length > 0 && (
                <p className="muted small">Depends on: {selected.dependencies.join(', ')}{withDeps ? ' — installed too' : ''}.</p>
              )}
              {selected.installedInTenants.length > 0 && (
                <p className="muted small">Active in: {selected.installedInTenants.join(', ')}.</p>
              )}

              <div className="catalog-install">
                <label>Install into
                  <select value={target} onChange={(e) => setTarget(e.target.value)}>
                    <option value="">Choose a tenant…</option>
                    {tenants.filter((t) => t.toLowerCase() !== 'platform').map((t) => (
                      <option key={t} value={t}>{t}</option>
                    ))}
                  </select>
                </label>
                <label className="catalog-deps">
                  <input type="checkbox" checked={withDeps} onChange={(e) => setWithDeps(e.target.checked)} />
                  with dependencies
                </label>
                <button onClick={install} disabled={!target || installing || alreadyThere}>
                  {installing ? 'Installing…' : alreadyThere ? 'Already in this tenant' : 'Install'}
                </button>
              </div>

              {installError && <p className="error small">{installError}</p>}
              {result && (
                <div className="agent-answer">
                  <p className="small"><strong>{result.activated.length}</strong> activated{result.skipped.length ? `, ${result.skipped.length} already present` : ''}{result.errors.length ? `, ${result.errors.length} error(s)` : ''}.</p>
                  {result.activated.length > 0 && <p className="muted small">Activated: {result.activated.join(', ')}</p>}
                  {result.errors.length > 0 && <p className="error small">{result.errors.join(' · ')}</p>}
                </div>
              )}
            </>
          )}
        </section>
      </div>
    </div>
  )
}
