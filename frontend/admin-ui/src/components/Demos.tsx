import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api'

// Demo/training twins (operator). One click stands up "{tenant}-demo" with the
// same skills and synthetic data that lights up the controls — for training and
// demos, without touching a client's real data.

interface TenantRow { tenantId: string; name: string }
interface DemoResult {
  demoTenantId: string
  created: boolean
  skillsInstalled: number
  recordsSubmitted: number
  renewalsCreated: number
  benchmarksCreated: number
  errors: string[]
}

export function Demos({ onOpen }: { onOpen: (tenantId: string) => void }) {
  const [tenants, setTenants] = useState<TenantRow[] | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [results, setResults] = useState<Record<string, DemoResult>>({})
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try { setTenants(await api.get<TenantRow[]>('/api/platform/tenants')) }
    catch (e) { setError(e instanceof ApiError ? e.message : String(e)); setTenants([]) }
  }, [])
  useEffect(() => { void load() }, [load])

  const ids = new Set((tenants ?? []).map((t) => t.tenantId.toLowerCase()))
  const real = (tenants ?? []).filter((t) =>
    t.tenantId.toLowerCase() !== 'platform' && !t.tenantId.toLowerCase().endsWith('-demo'))

  async function seed(id: string) {
    setBusy(id); setError(null)
    try {
      const r = await api.post<DemoResult>(`/api/platform/tenants/${id}/demo`)
      setResults((p) => ({ ...p, [id]: r }))
      await load()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    } finally { setBusy(null) }
  }

  const twinCount = real.filter((t) => ids.has(`${t.tenantId}-demo`)).length

  return (
    <div>
      <section className="panel">
        <h2>Demo twins</h2>
        <p className="muted">
          Create a demonstration copy of any tenant — same skills, plus synthetic records that fire its
          controls, demo renewals and benchmarks. Demo twins are separate tenants (<code>{'{tenant}-demo'}</code>);
          a client&apos;s real data is never touched. Regulated tenants&apos; demos are capped so they stay reachable.
        </p>
        {tenants && <p className="muted small">{twinCount} of {real.length} tenants have a demo twin.</p>}
        {error && <p className="error small">{error}</p>}
      </section>

      <section className="panel">
        {tenants === null ? <p className="muted">Loading tenants…</p> : (
          <table>
            <thead><tr><th>Tenant</th><th>Demo twin</th><th></th></tr></thead>
            <tbody>
              {real.map((t) => {
                const has = ids.has(`${t.tenantId}-demo`)
                const r = results[t.tenantId]
                return (
                  <tr key={t.tenantId}>
                    <td><strong>{t.name}</strong><div className="muted small">{t.tenantId}</div></td>
                    <td>
                      {has
                        ? <span className="state state-active">{t.tenantId}-demo</span>
                        : <span className="muted small">none</span>}
                      {r && (
                        <div className="muted small">
                          {r.created ? 'created' : 'topped up'}: {r.skillsInstalled} skills · {r.recordsSubmitted} records · {r.renewalsCreated} renewals · {r.benchmarksCreated} benchmarks
                          {r.errors.length > 0 && <span className="error-text"> · {r.errors.length} error(s)</span>}
                        </div>
                      )}
                    </td>
                    <td style={{ whiteSpace: 'nowrap' }}>
                      <button onClick={() => seed(t.tenantId)} disabled={busy === t.tenantId}>
                        {busy === t.tenantId ? 'Building…' : has ? 'Refresh demo' : 'Create demo twin'}
                      </button>
                      {has && <button className="secondary" style={{ marginLeft: '0.4rem' }} onClick={() => onOpen(`${t.tenantId}-demo`)}>Open →</button>}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        )}
      </section>
    </div>
  )
}
