import { Fragment, useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api'

interface BillingPlan {
  planId: string
  name: string
  currency: string
  baseMonthlyFee: number
  pricePerRecord: number
  pricePerDocumentArchived: number
  pricePerAiCall: number
  pricePerFeedRun: number
  pricePerActiveUser: number
  isDefault: boolean
}

interface TenantProfile {
  tenantId: string
  name: string
  status: string
  billingPlanId: string | null
}

interface Usage {
  recordsProcessed: number
  documentsArchived: number
  storageBytes: number
  aiCalls: number
  feedRuns: number
  activeUsers: number
}

interface LineItem {
  metric: string
  quantity: number
  unitPrice: number
  amount: number
}

interface Statement {
  id: string
  planId: string
  currency: string
  periodStart: string
  periodEnd: string
  lines: LineItem[]
  total: number
  generatedAt: string
  generatedBy: string
}

/// Platform-operator billing console: define plans with rates, assign them
/// to clients, watch metered utilization, and generate itemized statements.
export function Billing({ tenant, user }: { tenant: string; user: string }) {
  const [plans, setPlans] = useState<BillingPlan[]>([])
  const [tenants, setTenants] = useState<TenantProfile[]>([])
  const [selected, setSelected] = useState('')
  const [usage, setUsage] = useState<Usage | null>(null)
  const [statements, setStatements] = useState<Statement[]>([])
  const [openStatement, setOpenStatement] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [newPlan, setNewPlan] = useState({
    planId: '', name: '', baseMonthlyFee: '500', pricePerRecord: '0.05',
    pricePerDocumentArchived: '0.10', pricePerAiCall: '0.50', pricePerFeedRun: '0.25', pricePerActiveUser: '25',
  })

  const fail = (e: unknown) => setError(e instanceof ApiError ? e.message : String(e))

  const load = useCallback(async () => {
    try {
      setPlans(await api.get<BillingPlan[]>('/api/platform/billing/plans'))
      const all = await api.get<TenantProfile[]>('/api/platform/tenants')
      setTenants(all)
      if (all.length > 0) setSelected((s) => s || all[0].tenantId)
      setError(null)
    } catch (e) {
      fail(e)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load, tenant, user])

  const openTenant = useCallback(async (tenantId: string) => {
    if (!tenantId) return
    try {
      setUsage(await api.get<Usage>(`/api/platform/billing/tenants/${tenantId}/usage`))
      setStatements(await api.get<Statement[]>(`/api/platform/billing/tenants/${tenantId}/statements`))
      setError(null)
    } catch (e) {
      fail(e)
    }
  }, [])

  useEffect(() => {
    void openTenant(selected)
  }, [openTenant, selected])

  async function createPlan() {
    setError(null)
    setMessage(null)
    try {
      await api.post('/api/platform/billing/plans', {
        planId: newPlan.planId,
        name: newPlan.name,
        currency: 'USD',
        baseMonthlyFee: Number(newPlan.baseMonthlyFee),
        pricePerRecord: Number(newPlan.pricePerRecord),
        pricePerDocumentArchived: Number(newPlan.pricePerDocumentArchived),
        pricePerAiCall: Number(newPlan.pricePerAiCall),
        pricePerFeedRun: Number(newPlan.pricePerFeedRun),
        pricePerActiveUser: Number(newPlan.pricePerActiveUser),
      })
      setMessage(`Plan '${newPlan.planId}' created.`)
      setNewPlan({ ...newPlan, planId: '', name: '' })
      await load()
    } catch (e) {
      fail(e)
    }
  }

  async function assignPlan(planId: string) {
    if (!selected) return
    setError(null)
    try {
      await api.post(`/api/platform/billing/tenants/${selected}/plan`, { planId })
      setMessage(`Plan '${planId}' assigned to ${selected}.`)
      await load()
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
      setMessage('Statement generated for the month to date.')
      await openTenant(selected)
    } catch (e) {
      fail(e)
    }
  }

  const selectedProfile = tenants.find((t) => t.tenantId === selected)

  return (
    <div>
      {message && <div className="banner banner-ok">{message}</div>}
      {error && <div className="banner banner-error">{error}</div>}

      <section className="panel">
        <h2>Billing Plans <span className="state state-staged">PLATFORM</span></h2>
        <p className="muted small">
          How clients are charged: a base subscription plus unit prices for metered utilization.
          The meter reads straight from the operational tables — records, documents, AI calls,
          feed runs, active users — so a statement can never disagree with what actually happened.
        </p>
        <table>
          <thead>
            <tr><th>Plan</th><th>Base / month</th><th>Record</th><th>Document</th><th>AI call</th><th>Feed run</th><th>Active user</th></tr>
          </thead>
          <tbody>
            {plans.map((p) => (
              <tr key={p.planId}>
                <td><strong>{p.name}</strong> <span className="muted mono small">{p.planId}</span>{p.isDefault ? ' ⭐' : ''}</td>
                <td>{p.baseMonthlyFee} {p.currency}</td>
                <td>{p.pricePerRecord}</td>
                <td>{p.pricePerDocumentArchived}</td>
                <td>{p.pricePerAiCall}</td>
                <td>{p.pricePerFeedRun}</td>
                <td>{p.pricePerActiveUser}</td>
              </tr>
            ))}
          </tbody>
        </table>

        <h3>Add a plan</h3>
        <div className="form-row">
          <label>Plan id<input placeholder="enterprise" value={newPlan.planId}
            onChange={(e) => setNewPlan({ ...newPlan, planId: e.target.value })} /></label>
          <label>Name<input value={newPlan.name}
            onChange={(e) => setNewPlan({ ...newPlan, name: e.target.value })} /></label>
          <label>Base<input type="number" value={newPlan.baseMonthlyFee}
            onChange={(e) => setNewPlan({ ...newPlan, baseMonthlyFee: e.target.value })} /></label>
          <label>/record<input type="number" step="0.01" value={newPlan.pricePerRecord}
            onChange={(e) => setNewPlan({ ...newPlan, pricePerRecord: e.target.value })} /></label>
          <label>/document<input type="number" step="0.01" value={newPlan.pricePerDocumentArchived}
            onChange={(e) => setNewPlan({ ...newPlan, pricePerDocumentArchived: e.target.value })} /></label>
          <label>/AI call<input type="number" step="0.01" value={newPlan.pricePerAiCall}
            onChange={(e) => setNewPlan({ ...newPlan, pricePerAiCall: e.target.value })} /></label>
          <label>/feed<input type="number" step="0.01" value={newPlan.pricePerFeedRun}
            onChange={(e) => setNewPlan({ ...newPlan, pricePerFeedRun: e.target.value })} /></label>
          <label>/user<input type="number" value={newPlan.pricePerActiveUser}
            onChange={(e) => setNewPlan({ ...newPlan, pricePerActiveUser: e.target.value })} /></label>
          <button onClick={createPlan} disabled={!newPlan.planId.trim() || !newPlan.name.trim()}>Create plan</button>
        </div>
      </section>

      <section className="panel">
        <h2>Client utilization &amp; statements</h2>
        <div className="form-row">
          <label>Client<select value={selected} onChange={(e) => setSelected(e.target.value)}>
            {tenants.map((t) => <option key={t.tenantId} value={t.tenantId}>{t.name} ({t.tenantId})</option>)}
          </select></label>
          {selectedProfile && (
            <label>Plan<select value={selectedProfile.billingPlanId ?? 'standard'}
              onChange={(e) => assignPlan(e.target.value)}>
              {plans.map((p) => <option key={p.planId} value={p.planId}>{p.name}</option>)}
            </select></label>
          )}
          <button onClick={generateStatement} disabled={!selected}>Generate statement (month to date)</button>
        </div>

        {usage && (
          <div className="kpi-grid">
            {[
              [usage.recordsProcessed, 'Records processed'],
              [usage.documentsArchived, 'Documents archived'],
              [`${Math.round(usage.storageBytes / 1024)} KB`, 'Storage'],
              [usage.aiCalls, 'AI calls'],
              [usage.feedRuns, 'Feed runs'],
              [usage.activeUsers, 'Active users'],
            ].map(([value, label]) => (
              <div key={String(label)} className="panel kpi">
                <div className="kpi-value">{value as never}</div>
                <div className="muted small">{label} (month to date)</div>
              </div>
            ))}
          </div>
        )}

        <h3>Statements</h3>
        <table>
          <thead><tr><th>Period</th><th>Plan</th><th>Total</th><th>Generated</th><th></th></tr></thead>
          <tbody>
            {statements.map((s) => (
              <Fragment key={s.id}>
                <tr>
                  <td>{new Date(s.periodStart).toLocaleDateString()} → {new Date(s.periodEnd).toLocaleDateString()}</td>
                  <td>{s.planId}</td>
                  <td><strong>{s.total} {s.currency}</strong></td>
                  <td className="small">{new Date(s.generatedAt).toLocaleString()} by {s.generatedBy}</td>
                  <td><button onClick={() => setOpenStatement(openStatement === s.id ? null : s.id)}>
                    {openStatement === s.id ? 'Hide' : 'Line items'}
                  </button></td>
                </tr>
                {openStatement === s.id && (
                  <tr>
                    <td colSpan={5}>
                      <table>
                        <tbody>
                          {s.lines.map((l) => (
                            <tr key={l.metric}>
                              <td>{l.metric}</td>
                              <td>{l.quantity} × {l.unitPrice}</td>
                              <td><strong>{l.amount} {s.currency}</strong></td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </td>
                  </tr>
                )}
              </Fragment>
            ))}
            {statements.length === 0 && <tr><td colSpan={5} className="muted">No statements yet for this client.</td></tr>}
          </tbody>
        </table>
      </section>
    </div>
  )
}
