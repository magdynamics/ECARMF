import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api'

interface Integration {
  integrationId: string
  name: string
  applicationType: string
  connectorId: string
  unitId: string | null
  mode: string
  pullUrl: string | null
  pullIntervalMinutes: number | null
  status: string
  lastFeedAt: string | null
  lastFeedStatus: string | null
}

interface FeedRun {
  id: string
  integrationId: string
  trigger: string
  triggeredBy: string
  startedAt: string
  success: boolean
  recordsIngested: number
  error: string | null
}

interface OrgUnit {
  unitId: string
  name: string
  unitType: string
  status: string
}

interface Connector {
  connectorId: string
  name: string
  schemaTemplateId: string
}

/// Managed integrations with external applications: accounting, POS, billing,
/// real-estate management — configuration owns the relationship, the
/// connector owns how feeds become records, every run is history.
export function Integrations({ tenant, user }: { tenant: string; user: string }) {
  const [integrations, setIntegrations] = useState<Integration[]>([])
  const [runs, setRuns] = useState<FeedRun[]>([])
  const [connectors, setConnectors] = useState<Connector[]>([])
  const [units, setUnits] = useState<OrgUnit[]>([])
  const [appTypes, setAppTypes] = useState<string[]>([])
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [feedPayload, setFeedPayload] = useState('')
  const [feedTarget, setFeedTarget] = useState('')

  const [form, setForm] = useState({
    integrationId: '', name: '', applicationType: 'Accounting', connectorId: '',
    unitId: '', mode: 'push', pullUrl: '', pullIntervalMinutes: '', authSecret: '',
  })

  const fail = (e: unknown) => setError(e instanceof ApiError ? e.message : String(e))

  const load = useCallback(async () => {
    try {
      const list = await api.get<Integration[]>('/api/integrations')
      setIntegrations(list)
      setRuns(await api.get<FeedRun[]>('/api/integrations/runs?limit=20'))
      setAppTypes(await api.get<string[]>('/api/integrations/application-types'))
      const conns = await api.get<Connector[]>('/api/connectors')
      setConnectors(conns)
      try { setUnits((await api.get<OrgUnit[]>('/api/org-units')).filter((u) => u.status !== 'Archived')) } catch { setUnits([]) }
      if (list.length > 0 && !feedTarget) setFeedTarget(list[0].integrationId)
      setError(null)
    } catch (e) {
      fail(e)
    }
  }, [feedTarget])

  useEffect(() => {
    void load()
  }, [load, tenant, user])

  async function create() {
    setError(null)
    setMessage(null)
    try {
      await api.post('/api/integrations', {
        integrationId: form.integrationId,
        name: form.name,
        applicationType: form.applicationType,
        connectorId: form.connectorId || connectors[0]?.connectorId,
        unitId: form.unitId || null,
        mode: form.mode,
        pullUrl: form.mode === 'pull' ? form.pullUrl : null,
        pullIntervalMinutes: form.pullIntervalMinutes ? Number(form.pullIntervalMinutes) : null,
        authSecret: form.authSecret || null,
      })
      setMessage(`Integration '${form.integrationId}' configured.`)
      setForm({ integrationId: '', name: '', applicationType: 'Accounting', connectorId: '', unitId: '', mode: 'push', pullUrl: '', pullIntervalMinutes: '', authSecret: '' })
      await load()
    } catch (e) {
      fail(e)
    }
  }

  async function setStatus(id: string, status: string) {
    try {
      await api.post(`/api/integrations/${id}/status`, { status })
      await load()
    } catch (e) {
      fail(e)
    }
  }

  async function pushFeed() {
    setError(null)
    setMessage(null)
    try {
      const run = await api.post<FeedRun>(`/api/integrations/${feedTarget}/feed`, { rawPayload: feedPayload })
      setMessage(`Feed ingested ${run.recordsIngested} record(s) through '${feedTarget}'.`)
      setFeedPayload('')
      await load()
    } catch (e) {
      fail(e)
    }
  }

  async function pull(id: string) {
    setError(null)
    setMessage(null)
    try {
      const run = await api.post<FeedRun>(`/api/integrations/${id}/pull`)
      setMessage(`Pull ingested ${run.recordsIngested} record(s).`)
      await load()
    } catch (e) {
      fail(e)
    }
  }

  return (
    <div>
      {message && <div className="banner banner-ok">{message}</div>}
      {error && <div className="banner banner-error">{error}</div>}

      <section className="panel">
        <h2>Integrations <span className="state state-staged">SETUP</span></h2>
        <p className="muted small">
          Connect external applications — accounting, POS, billing, real-estate management — as
          managed feeds. Push: the application delivers to the platform. Pull: the platform fetches
          from the application's export endpoint (on demand or on a schedule). Every feed flows
          through a connector's schema template and lands in the source library.
        </p>

        <table>
          <thead>
            <tr><th>Integration</th><th>Type</th><th>Entity / location</th><th>Connector</th><th>Mode</th><th>Status</th><th>Last feed</th><th></th></tr>
          </thead>
          <tbody>
            {integrations.map((i) => (
              <tr key={i.integrationId}>
                <td><strong>{i.name}</strong> <span className="muted mono small">{i.integrationId}</span></td>
                <td>{i.applicationType}</td>
                <td>{i.unitId ? <span className="state state-staged">{i.unitId}</span> : <span className="muted small">all units</span>}</td>
                <td className="mono small">{i.connectorId}</td>
                <td>{i.mode}{i.pullIntervalMinutes ? ` (every ${i.pullIntervalMinutes}m)` : ''}</td>
                <td><span className={`state state-${i.status.toLowerCase()}`}>{i.status}</span></td>
                <td>
                  {i.lastFeedAt
                    ? `${new Date(i.lastFeedAt).toLocaleString()} — ${i.lastFeedStatus}`
                    : 'never'}
                </td>
                <td>
                  {i.mode === 'pull' && <button onClick={() => pull(i.integrationId)}>Pull now</button>}{' '}
                  {i.status === 'Active'
                    ? <button onClick={() => setStatus(i.integrationId, 'Paused')}>Pause</button>
                    : <button onClick={() => setStatus(i.integrationId, 'Active')}>Resume</button>}
                </td>
              </tr>
            ))}
            {integrations.length === 0 && <tr><td colSpan={8} className="muted">No integrations configured.</td></tr>}
          </tbody>
        </table>

        <h3>Add an integration</h3>
        <div className="form-row">
          <label>Id<input placeholder="quickbooks-main" value={form.integrationId}
            onChange={(e) => setForm({ ...form, integrationId: e.target.value })} /></label>
          <label>Name<input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} /></label>
          <label>Application<select value={form.applicationType}
            onChange={(e) => setForm({ ...form, applicationType: e.target.value })}>
            {appTypes.map((t) => <option key={t} value={t}>{t}</option>)}
          </select></label>
          <label>For entity / location<select value={form.unitId}
            title="Every record this integration delivers is attributed to this unit - e.g. the Chase feed of the Oak Lawn location. Leave as all units for tenant-wide sources like an HR system."
            onChange={(e) => setForm({ ...form, unitId: e.target.value })}>
            <option value="">All units (tenant-wide)</option>
            {units.map((u) => <option key={u.unitId} value={u.unitId}>{u.name} ({u.unitType})</option>)}
          </select></label>
          <label>Connector<select value={form.connectorId}
            onChange={(e) => setForm({ ...form, connectorId: e.target.value })}>
            <option value="">choose…</option>
            {connectors.map((c) => <option key={c.connectorId} value={c.connectorId}>{c.name} ({c.schemaTemplateId})</option>)}
          </select></label>
          <label>Mode<select value={form.mode} onChange={(e) => setForm({ ...form, mode: e.target.value })}>
            <option value="push">push (app delivers)</option>
            <option value="pull">pull (platform fetches)</option>
          </select></label>
          {form.mode === 'pull' && (
            <>
              <label>Pull URL<input placeholder="https://…/export" value={form.pullUrl}
                onChange={(e) => setForm({ ...form, pullUrl: e.target.value })} /></label>
              <label>Every (min)<input type="number" value={form.pullIntervalMinutes}
                onChange={(e) => setForm({ ...form, pullIntervalMinutes: e.target.value })} /></label>
              <label>Auth secret<input type="password" value={form.authSecret}
                onChange={(e) => setForm({ ...form, authSecret: e.target.value })} /></label>
            </>
          )}
          <button onClick={create} disabled={!form.integrationId.trim() || !form.name.trim()}>Add</button>
        </div>

        {integrations.length > 0 && (
          <>
            <h3>Push a feed (simulate the application)</h3>
            <div className="form-row">
              <label>Integration<select value={feedTarget} onChange={(e) => setFeedTarget(e.target.value)}>
                {integrations.map((i) => <option key={i.integrationId} value={i.integrationId}>{i.name}</option>)}
              </select></label>
              <button onClick={pushFeed} disabled={!feedPayload.trim()}>Send feed</button>
            </div>
            <textarea rows={3} placeholder="Raw payload exactly as the application produces it…"
              value={feedPayload} onChange={(e) => setFeedPayload(e.target.value)} />
          </>
        )}

        <h3>Recent feed runs</h3>
        <table>
          <thead><tr><th>When</th><th>Integration</th><th>Trigger</th><th>By</th><th>Result</th></tr></thead>
          <tbody>
            {runs.map((r) => (
              <tr key={r.id}>
                <td>{new Date(r.startedAt).toLocaleString()}</td>
                <td className="mono small">{r.integrationId}</td>
                <td>{r.trigger}</td>
                <td className="small">{r.triggeredBy}</td>
                <td>
                  {r.success
                    ? <span className="state state-approved">{r.recordsIngested} record(s)</span>
                    : <span className="state state-failed" title={r.error ?? ''}>failed</span>}
                  {!r.success && r.error && <span className="muted small"> {r.error.slice(0, 80)}</span>}
                </td>
              </tr>
            ))}
            {runs.length === 0 && <tr><td colSpan={5} className="muted">No feed runs yet.</td></tr>}
          </tbody>
        </table>
      </section>
    </div>
  )
}
