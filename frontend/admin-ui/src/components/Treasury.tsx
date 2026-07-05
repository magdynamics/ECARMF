import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api'

interface SweepAccount {
  accountId: string
  name: string
  unitId: string | null
  institution: string
  kind: string
  destinationAccountId: string | null
  approvedThreshold: number | null
  approvedBy: string | null
  proposedThreshold: number | null
  proposalReasoning: string | null
  enabled: boolean
  lastObservedBalance: number | null
  lastSweepAt: string | null
}

/// AI Treasury: rolling threshold proposals (Recommend-Only), autonomous
/// sweeps against standing approved thresholds, payroll alert-only.
export function Treasury({ tenant, user }: { tenant: string; user: string }) {
  const [accounts, setAccounts] = useState<SweepAccount[]>([])
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [balances, setBalances] = useState<Record<string, string>>({})
  const [overrides, setOverrides] = useState<Record<string, string>>({})
  const [form, setForm] = useState({
    accountId: '', name: '', unitId: '', institution: 'Bank of America',
    kind: 'Operating', destinationAccountId: 'corporate-operating',
  })

  const fail = (e: unknown) => setError(e instanceof ApiError ? e.message : String(e))

  const load = useCallback(async () => {
    try {
      setAccounts(await api.get<SweepAccount[]>('/api/treasury/accounts'))
      setError(null)
    } catch (e) {
      fail(e)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load, tenant, user])

  async function create() {
    setError(null)
    setMessage(null)
    try {
      await api.post('/api/treasury/accounts', {
        accountId: form.accountId,
        name: form.name,
        unitId: form.unitId || null,
        institution: form.institution,
        kind: form.kind,
        destinationAccountId: form.kind === 'Operating' ? form.destinationAccountId || null : null,
        enabled: true,
      })
      setMessage(`Account '${form.name}' under treasury management (${form.kind}).`)
      setForm({ ...form, accountId: '', name: '', unitId: '' })
      await load()
    } catch (e) {
      fail(e)
    }
  }

  async function approve(a: SweepAccount) {
    setError(null)
    try {
      const raw = overrides[a.accountId]
      await api.post(`/api/treasury/accounts/${a.accountId}/approve-threshold`, {
        overrideValue: raw ? Number(raw) : null,
      })
      setMessage(`Threshold for '${a.name}' approved — sweeps now execute against it autonomously.`)
      setOverrides({ ...overrides, [a.accountId]: '' })
      await load()
    } catch (e) {
      fail(e)
    }
  }

  async function observe(a: SweepAccount) {
    setError(null)
    const raw = balances[a.accountId]
    if (!raw) return
    try {
      const r = await api.post<{ sweepExecuted: boolean; sweepAmount: number | null; payrollAlertRaised: boolean }>(
        `/api/treasury/accounts/${a.accountId}/observe`, { balance: Number(raw) })
      setMessage(r.sweepExecuted
        ? `Balance recorded — AUTONOMOUS SWEEP of ${r.sweepAmount?.toLocaleString()} to '${a.destinationAccountId}' (see Allocations for the full reasoning).`
        : r.payrollAlertRaised
          ? 'Balance recorded — payroll account flagged high; no sweep (by design).'
          : 'Balance recorded — under threshold, nothing to sweep.')
      setBalances({ ...balances, [a.accountId]: '' })
      await load()
    } catch (e) {
      fail(e)
    }
  }

  async function recalculate() {
    setError(null)
    try {
      const r = await api.post<{ proposals: number }>('/api/treasury/accounts/recalculate', {})
      setMessage(`AI treasury pass complete: ${r.proposals} threshold proposal(s) raised for review.`)
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
        <h2>AI Treasury <span className="state state-staged">SETUP</span></h2>
        <p className="muted small">
          Operating accounts sweep their overage above a <strong>standing approved threshold</strong> to
          the corporate account autonomously — every sweep fully reasoned in Allocations. The
          threshold itself is continuously re-proposed by the AI from trailing balances and only
          takes effect after your approval (Recommend-Only). Payroll accounts are never swept —
          an abnormally high payroll balance raises an alert instead.{' '}
          <button className="secondary" onClick={recalculate}>Run AI treasury pass now</button>
        </p>

        <table>
          <thead>
            <tr><th>Account</th><th>Kind</th><th>Approved threshold</th><th>AI proposal</th><th>Last balance</th><th>Observe balance</th></tr>
          </thead>
          <tbody>
            {accounts.map((a) => (
              <tr key={a.accountId}>
                <td>
                  <strong>{a.name}</strong>{' '}
                  <span className="muted mono small">{a.accountId}</span>
                  {a.lastSweepAt && <div className="muted small">last sweep {new Date(a.lastSweepAt).toLocaleString()}</div>}
                </td>
                <td>{a.kind === 'Payroll'
                  ? <span className="state state-flagged">Payroll — never swept</span>
                  : <span className="state state-approved">Operating</span>}</td>
                <td>{a.approvedThreshold !== null
                  ? <><strong>{a.approvedThreshold.toLocaleString()}</strong> <span className="muted small">by {a.approvedBy}</span></>
                  : <span className="muted">none — sweeps off</span>}</td>
                <td>
                  {a.proposedThreshold !== null ? (
                    <div title={a.proposalReasoning ?? ''}>
                      <span className="state state-pending">{a.proposedThreshold.toLocaleString()} proposed</span>{' '}
                      <input placeholder="override…" value={overrides[a.accountId] ?? ''} style={{ width: 80 }}
                        onChange={(e) => setOverrides({ ...overrides, [a.accountId]: e.target.value })} />{' '}
                      <button onClick={() => approve(a)}>Approve</button>
                    </div>
                  ) : <span className="muted small">—</span>}
                </td>
                <td>{a.lastObservedBalance !== null ? a.lastObservedBalance.toLocaleString() : '—'}</td>
                <td>
                  <input type="number" placeholder="balance" value={balances[a.accountId] ?? ''} style={{ width: 110 }}
                    onChange={(e) => setBalances({ ...balances, [a.accountId]: e.target.value })} />{' '}
                  <button className="secondary" onClick={() => observe(a)} disabled={!balances[a.accountId]}>Record</button>
                </td>
              </tr>
            ))}
            {accounts.length === 0 && (
              <tr><td colSpan={6} className="muted">
                No accounts under treasury management yet — add the operating and payroll accounts below.
              </td></tr>
            )}
          </tbody>
        </table>

        <h3>Put an account under treasury management</h3>
        <div className="form-row">
          <label>Account id<input placeholder="oak-lawn-operating" value={form.accountId}
            onChange={(e) => setForm({ ...form, accountId: e.target.value })} /></label>
          <label>Name<input placeholder="Oak Lawn — Operating" value={form.name}
            onChange={(e) => setForm({ ...form, name: e.target.value })} /></label>
          <label>Org unit<input placeholder="oak-lawn (optional)" value={form.unitId}
            onChange={(e) => setForm({ ...form, unitId: e.target.value })} /></label>
          <label>Institution<input value={form.institution}
            onChange={(e) => setForm({ ...form, institution: e.target.value })} /></label>
          <label>Kind<select value={form.kind} onChange={(e) => setForm({ ...form, kind: e.target.value })}>
            <option>Operating</option><option>Payroll</option>
          </select></label>
          {form.kind === 'Operating' && (
            <label>Sweep destination<input value={form.destinationAccountId}
              onChange={(e) => setForm({ ...form, destinationAccountId: e.target.value })} /></label>
          )}
          <button onClick={create} disabled={!form.accountId.trim() || !form.name.trim()}>Add</button>
        </div>
      </section>
    </div>
  )
}
