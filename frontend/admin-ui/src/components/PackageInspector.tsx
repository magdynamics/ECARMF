import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api'
import type { OperationResult, PackageDetail, PackageSummary } from '../types'

export function PackageInspector({ tenant, user }: { tenant: string; user: string }) {
  const [packages, setPackages] = useState<PackageSummary[]>([])
  const [selected, setSelected] = useState<PackageSummary | null>(null)
  const [detail, setDetail] = useState<PackageDetail | null>(null)
  const [manifestJson, setManifestJson] = useState('')
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    try {
      setError(null)
      setPackages(await api.get<PackageSummary[]>('/api/packages'))
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }, [])

  useEffect(() => {
    setSelected(null)
    setDetail(null)
    setMessage(null)
    void refresh()
  }, [refresh, tenant, user])

  useEffect(() => {
    if (!selected) {
      setDetail(null)
      return
    }
    api
      .get<PackageDetail>(`/api/packages/${selected.packageId}/${selected.packageVersion}`)
      .then(setDetail)
      .catch((e) => setError(e instanceof ApiError ? e.message : String(e)))
  }, [selected])

  async function upload() {
    setMessage(null)
    setError(null)
    try {
      const manifest = JSON.parse(manifestJson)
      const result = await api.post<OperationResult>('/api/packages', manifest)
      setMessage(`Staged: ${manifest.packageId} ${manifest.packageVersion} (${result.state})`)
      setManifestJson('')
      await refresh()
    } catch (e) {
      if (e instanceof SyntaxError) setError(`Manifest is not valid JSON: ${e.message}`)
      else setError(e instanceof ApiError ? e.message : String(e))
    }
  }

  async function lifecycle(pkg: PackageSummary, action: 'activate' | 'deactivate') {
    setMessage(null)
    setError(null)
    try {
      await api.post<OperationResult>(
        `/api/packages/${pkg.packageId}/${pkg.packageVersion}/${action}`,
      )
      setMessage(`${action === 'activate' ? 'Activated' : 'Deactivated'}: ${pkg.packageId} ${pkg.packageVersion}`)
      await refresh()
      if (selected?.packageId === pkg.packageId) setSelected(null)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }

  return (
    <div className="two-col">
      <section className="panel">
        <h2>Knowledge Packages</h2>
        {error && <div className="banner banner-error">{error}</div>}
        {message && <div className="banner banner-ok">{message}</div>}
        <table>
          <thead>
            <tr>
              <th>Package</th>
              <th>Version</th>
              <th>State</th>
              <th>Contributes</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {packages.length === 0 && (
              <tr>
                <td colSpan={5} className="muted">
                  No packages loaded for this tenant.
                </td>
              </tr>
            )}
            {packages.map((p) => (
              <tr
                key={`${p.packageId}@${p.packageVersion}`}
                className={selected === p ? 'selected' : ''}
                onClick={() => setSelected(p)}
              >
                <td>
                  <strong>{p.name}</strong>
                  <div className="muted">{p.packageId}</div>
                </td>
                <td>{p.packageVersion}</td>
                <td>
                  <span className={`state state-${p.state.toLowerCase()}`}>{p.state}</span>
                  {p.statusDetail && <div className="muted small">{p.statusDetail}</div>}
                </td>
                <td className="muted small">
                  {p.entities}E / {p.rules}R / {p.events}Ev / {p.capabilities}C
                </td>
                <td>
                  {p.state === 'Staged' || p.state === 'Deactivated' ? (
                    <button onClick={(e) => (e.stopPropagation(), lifecycle(p, 'activate'))}>
                      Activate
                    </button>
                  ) : p.state === 'Active' ? (
                    <button
                      className="secondary"
                      onClick={(e) => (e.stopPropagation(), lifecycle(p, 'deactivate'))}
                    >
                      Deactivate
                    </button>
                  ) : null}
                </td>
              </tr>
            ))}
          </tbody>
        </table>

        <h3>Upload manifest</h3>
        <textarea
          rows={8}
          placeholder='Paste a Knowledge Package manifest (JSON), e.g. the contents of packages/treasury-controls-v1.json'
          value={manifestJson}
          onChange={(e) => setManifestJson(e.target.value)}
        />
        <button disabled={!manifestJson.trim()} onClick={upload}>
          Upload package
        </button>
      </section>

      <section className="panel">
        <h2>Inspector</h2>
        {!detail && <p className="muted">Select a package to inspect its manifest.</p>}
        {detail && (
          <>
            <p>
              <span className={`state state-${detail.state.toLowerCase()}`}>{detail.state}</span>{' '}
              <strong>{detail.manifest.name}</strong> — {detail.manifest.packageId}{' '}
              {detail.manifest.packageVersion} by {detail.manifest.publisher}
            </p>
            {detail.manifest.description && <p className="muted">{detail.manifest.description}</p>}

            <h3>Entities ({detail.manifest.entities.length})</h3>
            {detail.manifest.entities.map((entity) => (
              <div key={entity.entityTypeName} className="card">
                <strong>{entity.entityTypeName}</strong>
                <div className="muted small">
                  {entity.attributes
                    .map((a) => `${a.name}: ${a.dataType}${a.required ? ' (required)' : ''}`)
                    .join(', ')}
                </div>
              </div>
            ))}

            <h3>Events ({detail.manifest.events.length})</h3>
            {detail.manifest.events.map((ev) => (
              <div key={ev.eventName} className="card">
                <strong>{ev.eventName}</strong>
                {ev.description && <div className="muted small">{ev.description}</div>}
              </div>
            ))}

            <h3>Rules ({detail.manifest.rules.length})</h3>
            {detail.manifest.rules.map((rule) => (
              <div key={rule.ruleId} className="card">
                <strong>{rule.ruleId}</strong> — {rule.name}
                <div className="small">
                  on <code>{rule.triggerEvent}</code> →{' '}
                  <span className={`state state-${rule.outcomeOnMatch.toLowerCase()}`}>
                    {rule.outcomeOnMatch}
                  </span>
                </div>
                <div className="muted small">
                  {rule.conditions.map((c) => `${c.field} ${c.operator} ${c.value}`).join(' AND ')}
                </div>
              </div>
            ))}

            <h3>Capabilities ({detail.manifest.capabilities.length})</h3>
            {detail.manifest.capabilities.map((cap) => (
              <div key={cap.capabilityId} className="card">
                <strong>{cap.capabilityId}</strong>
                {cap.description && <div className="muted small">{cap.description}</div>}
              </div>
            ))}
          </>
        )}
      </section>
    </div>
  )
}
