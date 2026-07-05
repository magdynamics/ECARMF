import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api'

interface TenantProfile {
  tenantId: string
  name: string
  industry: string | null
  contactName: string | null
  contactEmail: string | null
  status: string
  billingPlanId: string | null
  createdAt: string
}

interface TenantUser {
  identifier: string
  displayName: string
  isSystemActor: boolean
  roles: string[]
  status: string
  email: string | null
  phone: string | null
  jobTitle: string | null
  hasCredential: boolean
}

interface BillingPlan {
  planId: string
  name: string
  baseMonthlyFee: number
}

interface Usage {
  recordsProcessed: number
  documentsArchived: number
  storageBytes: number
  aiCalls: number
  feedRuns: number
  activeUsers: number
}

interface Statement {
  id: string
  periodStart: string
  periodEnd: string
  total: number
  currency: string
  planId: string
  generatedAt: string
}

const ROLES = [
  'ExecutiveOwner',
  'PlatformAdministrator',
  'VentureManager',
  'TreasuryOfficer',
  'RiskComplianceOfficer',
  'Auditor',
  'ConnectorOwner',
]

/// Platform-operator console: onboard client tenants, manage their contacts
/// as users, issue access credentials, and bill utilization.
export function Clients({ tenant, user }: { tenant: string; user: string }) {
  const [tenants, setTenants] = useState<TenantProfile[]>([])
  const [plans, setPlans] = useState<BillingPlan[]>([])
  const [selected, setSelected] = useState<string | null>(null)
  const [tenantUsers, setTenantUsers] = useState<TenantUser[]>([])
  const [usage, setUsage] = useState<Usage | null>(null)
  const [statements, setStatements] = useState<Statement[]>([])
  const [issuedKey, setIssuedKey] = useState<{ identifier: string; accessKey: string } | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const [newTenant, setNewTenant] = useState({ tenantId: '', name: '', industry: '', contactName: '', contactEmail: '' })
  const [newUser, setNewUser] = useState({ identifier: '', displayName: '', role: 'ExecutiveOwner', email: '', phone: '', jobTitle: '' })
  const [importJson, setImportJson] = useState('')
  const [importResult, setImportResult] = useState<{ tenantId: string; status: string; users?: { identifier: string; status: string; accessKey?: string }[] }[] | null>(null)
  const [importing, setImporting] = useState(false)

  const fail = (e: unknown) => setError(e instanceof ApiError ? e.message : String(e))

  const load = useCallback(async () => {
    try {
      setTenants(await api.get<TenantProfile[]>('/api/platform/tenants'))
      setPlans(await api.get<BillingPlan[]>('/api/platform/billing/plans'))
      setError(null)
    } catch (e) {
      fail(e)
    }
  }, [])

  useEffect(() => {
    setSelected(null)
    void load()
  }, [load, tenant, user])

  async function openTenant(tenantId: string) {
    setSelected(tenantId)
    setIssuedKey(null)
    setMessage(null)
    try {
      setTenantUsers(await api.get<TenantUser[]>(`/api/platform/tenants/${tenantId}/users`))
      setUsage(await api.get<Usage>(`/api/platform/billing/tenants/${tenantId}/usage`))
      setStatements(await api.get<Statement[]>(`/api/platform/billing/tenants/${tenantId}/statements`))
    } catch (e) {
      fail(e)
    }
  }

  async function createTenant() {
    setError(null)
    try {
      await api.post('/api/platform/tenants', {
        tenantId: newTenant.tenantId,
        name: newTenant.name,
        industry: newTenant.industry || null,
        contactName: newTenant.contactName || null,
        contactEmail: newTenant.contactEmail || null,
      })
      setMessage(`Tenant '${newTenant.tenantId}' onboarded.`)
      setNewTenant({ tenantId: '', name: '', industry: '', contactName: '', contactEmail: '' })
      await load()
    } catch (e) {
      fail(e)
    }
  }

  async function setTenantStatus(tenantId: string, status: string) {
    setError(null)
    try {
      await api.post(`/api/platform/tenants/${tenantId}/status`, { status })
      await load()
    } catch (e) {
      fail(e)
    }
  }

  async function createUser() {
    if (!selected) return
    setError(null)
    try {
      const created = await api.post<{ identifier: string; accessKey: string }>(
        `/api/platform/tenants/${selected}/users`,
        {
          identifier: newUser.identifier,
          displayName: newUser.displayName,
          role: newUser.role,
          email: newUser.email || null,
          phone: newUser.phone || null,
          jobTitle: newUser.jobTitle || null,
        },
      )
      setIssuedKey({ identifier: created.identifier, accessKey: created.accessKey })
      setNewUser({ identifier: '', displayName: '', role: 'ExecutiveOwner', email: '', phone: '', jobTitle: '' })
      await openTenant(selected)
    } catch (e) {
      fail(e)
    }
  }

  async function rotateKey(identifier: string) {
    if (!selected) return
    setError(null)
    try {
      const rotated = await api.post<{ identifier: string; accessKey: string }>(
        `/api/platform/tenants/${selected}/users/${encodeURIComponent(identifier)}/rotate-key`,
      )
      setIssuedKey(rotated)
    } catch (e) {
      fail(e)
    }
  }

  async function setUserStatus(identifier: string, status: string) {
    if (!selected) return
    setError(null)
    try {
      await api.post(`/api/platform/tenants/${selected}/users/${encodeURIComponent(identifier)}/status`, { status })
      await openTenant(selected)
    } catch (e) {
      fail(e)
    }
  }

  async function assignPlan(planId: string) {
    if (!selected) return
    try {
      await api.post(`/api/platform/billing/tenants/${selected}/plan`, { planId })
      await load()
      setMessage(`Plan '${planId}' assigned to ${selected}.`)
    } catch (e) {
      fail(e)
    }
  }

  async function generateStatement() {
    if (!selected) return
    setError(null)
    try {
      const now = new Date()
      const start = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 1))
      await api.post(`/api/platform/billing/tenants/${selected}/statements`, {
        periodStart: start.toISOString(),
        periodEnd: now.toISOString(),
      })
      setStatements(await api.get<Statement[]>(`/api/platform/billing/tenants/${selected}/statements`))
      setMessage('Statement generated for the month to date.')
    } catch (e) {
      fail(e)
    }
  }

  async function bulkImport() {
    setError(null)
    setMessage(null)
    setImporting(true)
    try {
      const clients = JSON.parse(importJson)
      const result = await api.post<{ imported: number; results: typeof importResult }>(
        '/api/platform/tenants/import',
        { clients: Array.isArray(clients) ? clients : clients.clients },
      )
      setImportResult(result.results)
      setImportJson('')
      setMessage(`Import processed ${result.imported} client(s). Copy the issued access keys below — they are shown once.`)
      await load()
    } catch (e) {
      if (e instanceof SyntaxError) setError(`Import is not valid JSON: ${e.message}`)
      else fail(e)
    } finally {
      setImporting(false)
    }
  }

  const selectedProfile = tenants.find((t) => t.tenantId === selected)

  return (
    <div>
      {message && <div className="banner banner-ok">{message}</div>}
      {error && <div className="banner banner-error">{error}</div>}

      {issuedKey && (
        <div className="banner banner-ok">
          Access key for <strong>{issuedKey.identifier}</strong> (shown once — copy it now):{' '}
          <code className="mono">{issuedKey.accessKey}</code>
        </div>
      )}

      <section className="panel">
        <h2>Clients <span className="state state-staged">PLATFORM</span></h2>
        <p className="muted small">
          We operate the platform for our clients: each client is a tenant with isolated data,
          users, credentials, AI configuration, and billing. Requires the operator tenant
          (<code>platform</code>) — client administrators cannot see this screen.
        </p>

        <table>
          <thead>
            <tr><th>Tenant</th><th>Client</th><th>Industry</th><th>Contact</th><th>Status</th><th>Plan</th><th></th></tr>
          </thead>
          <tbody>
            {tenants.map((t) => (
              <tr key={t.tenantId} className={selected === t.tenantId ? 'selected' : ''}>
                <td className="mono">{t.tenantId}</td>
                <td>{t.name}</td>
                <td>{t.industry ?? '—'}</td>
                <td>{t.contactName ?? '—'} {t.contactEmail ? `(${t.contactEmail})` : ''}</td>
                <td><span className={`state state-${t.status.toLowerCase()}`}>{t.status}</span></td>
                <td>{t.billingPlanId ?? 'standard'}</td>
                <td>
                  <button onClick={() => openTenant(t.tenantId)}>Manage</button>{' '}
                  {t.status === 'Active' ? (
                    <button onClick={() => setTenantStatus(t.tenantId, 'Suspended')}>Suspend</button>
                  ) : (
                    <button onClick={() => setTenantStatus(t.tenantId, 'Active')}>Reactivate</button>
                  )}
                </td>
              </tr>
            ))}
            {tenants.length === 0 && <tr><td colSpan={7} className="muted">No clients onboarded yet.</td></tr>}
          </tbody>
        </table>

        <h3>Onboard a client</h3>
        <div className="form-row">
          <label>Tenant id<input placeholder="acme-capital" value={newTenant.tenantId}
            onChange={(e) => setNewTenant({ ...newTenant, tenantId: e.target.value })} /></label>
          <label>Company name<input value={newTenant.name}
            onChange={(e) => setNewTenant({ ...newTenant, name: e.target.value })} /></label>
          <label>Industry<input value={newTenant.industry}
            onChange={(e) => setNewTenant({ ...newTenant, industry: e.target.value })} /></label>
          <label>Contact name<input value={newTenant.contactName}
            onChange={(e) => setNewTenant({ ...newTenant, contactName: e.target.value })} /></label>
          <label>Contact email<input value={newTenant.contactEmail}
            onChange={(e) => setNewTenant({ ...newTenant, contactEmail: e.target.value })} /></label>
          <button onClick={createTenant} disabled={!newTenant.tenantId.trim() || !newTenant.name.trim()}>
            Onboard client
          </button>
        </div>

        <h3>Bulk import your existing client base</h3>
        <p className="muted small">
          Paste a JSON array of clients (with their contacts) and onboard them in one operation —
          tenants created, identities seeded, contacts provisioned, access keys issued. Existing
          entries are skipped, never overwritten. Runs entirely on this machine.
        </p>
        <textarea
          rows={5}
          placeholder={'[\n  { "tenantId": "acme-capital", "name": "Acme Capital LLC", "industry": "PrivateEquity",\n    "users": [ { "identifier": "jane@acmecap.com", "displayName": "Jane Chen", "role": "ExecutiveOwner", "email": "jane@acmecap.com" } ] }\n]'}
          value={importJson}
          onChange={(e) => setImportJson(e.target.value)}
        />
        <div className="form-row">
          <button onClick={bulkImport} disabled={importing || !importJson.trim()}>
            {importing ? 'Importing…' : 'Import clients'}
          </button>
        </div>
        {importResult && (
          <table>
            <thead><tr><th>Tenant</th><th>Status</th><th>Users &amp; issued keys (copy now — shown once)</th></tr></thead>
            <tbody>
              {importResult.map((r) => (
                <tr key={r.tenantId}>
                  <td className="mono">{r.tenantId}</td>
                  <td><span className={`state state-${r.status === 'created' ? 'approved' : 'flagged'}`}>{r.status}</span></td>
                  <td className="small">
                    {(r.users ?? []).map((u) => (
                      <div key={u.identifier}>
                        {u.identifier}: {u.status}
                        {u.accessKey && <> — <code className="mono">{u.accessKey}</code></>}
                      </div>
                    ))}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      {selected && selectedProfile && (
        <section className="panel">
          <h2>{selectedProfile.name} <span className="muted small">({selected})</span></h2>

          <h3>Users &amp; contacts</h3>
          <table>
            <thead>
              <tr><th>Identifier</th><th>Name</th><th>Role</th><th>Contact</th><th>Status</th><th>Credential</th><th></th></tr>
            </thead>
            <tbody>
              {tenantUsers.map((u) => (
                <tr key={u.identifier}>
                  <td className="mono">{u.identifier}</td>
                  <td>{u.displayName}{u.jobTitle ? ` — ${u.jobTitle}` : ''}</td>
                  <td>{u.roles.join(', ')}</td>
                  <td>{u.email ?? '—'} {u.phone ? `· ${u.phone}` : ''}</td>
                  <td><span className={`state state-${u.status.toLowerCase()}`}>{u.status}</span></td>
                  <td>{u.isSystemActor ? 'AI actor' : u.hasCredential ? 'Issued' : 'None'}</td>
                  <td>
                    {!u.isSystemActor && (
                      <>
                        <button onClick={() => rotateKey(u.identifier)}>Rotate key</button>{' '}
                        {u.status === 'Active' ? (
                          <button onClick={() => setUserStatus(u.identifier, 'Disabled')}>Disable</button>
                        ) : (
                          <button onClick={() => setUserStatus(u.identifier, 'Active')}>Enable</button>
                        )}
                      </>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          <h3>Add a contact / user</h3>
          <div className="form-row">
            <label>Identifier (email)<input value={newUser.identifier}
              onChange={(e) => setNewUser({ ...newUser, identifier: e.target.value })} /></label>
            <label>Display name<input value={newUser.displayName}
              onChange={(e) => setNewUser({ ...newUser, displayName: e.target.value })} /></label>
            <label>Role<select value={newUser.role} onChange={(e) => setNewUser({ ...newUser, role: e.target.value })}>
              {ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
            </select></label>
            <label>Email<input value={newUser.email}
              onChange={(e) => setNewUser({ ...newUser, email: e.target.value })} /></label>
            <label>Phone<input value={newUser.phone}
              onChange={(e) => setNewUser({ ...newUser, phone: e.target.value })} /></label>
            <label>Job title<input value={newUser.jobTitle}
              onChange={(e) => setNewUser({ ...newUser, jobTitle: e.target.value })} /></label>
            <button onClick={createUser} disabled={!newUser.identifier.trim()}>Provision user + issue key</button>
          </div>

          <h3>Billing &amp; utilization</h3>
          {usage && (
            <p className="small">
              Month to date: <strong>{usage.recordsProcessed}</strong> records ·{' '}
              <strong>{usage.documentsArchived}</strong> documents ({Math.round(usage.storageBytes / 1024)} KB) ·{' '}
              <strong>{usage.aiCalls}</strong> AI calls · <strong>{usage.feedRuns}</strong> feed runs ·{' '}
              <strong>{usage.activeUsers}</strong> active users
            </p>
          )}
          <div className="form-row">
            <label>Plan<select value={selectedProfile.billingPlanId ?? 'standard'}
              onChange={(e) => assignPlan(e.target.value)}>
              {plans.map((p) => <option key={p.planId} value={p.planId}>{p.name} (base {p.baseMonthlyFee})</option>)}
            </select></label>
            <button onClick={generateStatement}>Generate statement (month to date)</button>
          </div>
          {statements.length > 0 && (
            <table>
              <thead><tr><th>Period</th><th>Plan</th><th>Total</th><th>Generated</th></tr></thead>
              <tbody>
                {statements.map((s) => (
                  <tr key={s.id}>
                    <td>{new Date(s.periodStart).toLocaleDateString()} → {new Date(s.periodEnd).toLocaleDateString()}</td>
                    <td>{s.planId}</td>
                    <td><strong>{s.total} {s.currency}</strong></td>
                    <td>{new Date(s.generatedAt).toLocaleString()}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
      )}
    </div>
  )
}
