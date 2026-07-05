import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api'

interface Renewal {
  id: string
  name: string
  category: string
  counterparty: string | null
  reference: string | null
  notes: string | null
  dueDate: string
  recurrenceMonths: number | null
  leadTimeDays: number[]
  notifyRole: string
  createTask: boolean
  status: string
  renewalCount: number
  lastRenewedAt: string | null
}

const CATEGORIES = ['License', 'Insurance', 'Loan', 'Lease', 'Corporate', 'Contract', 'Other']
const ROLES = ['ExecutiveOwner', 'RiskComplianceOfficer', 'TreasuryOfficer', 'PlatformAdministrator', 'Auditor']

function daysUntil(dueDate: string): number {
  return Math.ceil((new Date(dueDate).getTime() - Date.now()) / 86400000)
}

function dueBadge(r: Renewal) {
  if (r.status !== 'Active') return <span className="state state-deactivated">{r.status}</span>
  const days = daysUntil(r.dueDate)
  if (days < 0) return <span className="state state-failed">OVERDUE {-days}d</span>
  const smallest = Math.min(...r.leadTimeDays)
  if (days <= smallest) return <span className="state state-failed">{days}d left</span>
  if (days <= Math.max(...r.leadTimeDays)) return <span className="state state-flagged">{days}d left</span>
  return <span className="state state-approved">{days}d left</span>
}

/// The calendar watchdog: licenses, insurance, loans, leases, corporate
/// registrations — anything that lapses if nobody acts. Escalating alerts
/// fire as the due date approaches; nothing expires unannounced.
export function Renewals({ tenant, user }: { tenant: string; user: string }) {
  const [renewals, setRenewals] = useState<Renewal[]>([])
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [showClosed, setShowClosed] = useState(false)
  const [form, setForm] = useState({
    name: '', category: 'License', counterparty: '', reference: '',
    dueDate: '', recurrenceMonths: '12', leadTimeDays: '90,30,7',
    notifyRole: 'ExecutiveOwner', createTask: true,
  })

  const fail = (e: unknown) => setError(e instanceof ApiError ? e.message : String(e))

  const load = useCallback(async () => {
    try {
      setRenewals(await api.get<Renewal[]>('/api/renewals'))
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
      const ladder = form.leadTimeDays.split(',').map((s) => Number(s.trim())).filter((n) => !isNaN(n))
      await api.post('/api/renewals', {
        name: form.name,
        category: form.category,
        counterparty: form.counterparty || null,
        reference: form.reference || null,
        notes: null,
        dueDate: new Date(form.dueDate).toISOString(),
        recurrenceMonths: form.recurrenceMonths ? Number(form.recurrenceMonths) : null,
        leadTimeDays: ladder,
        notifyRole: form.notifyRole,
        createTask: form.createTask,
      })
      setMessage(`'${form.name}' is now watched — warnings at ${form.leadTimeDays} day(s) before ${form.dueDate}.`)
      setForm({ ...form, name: '', counterparty: '', reference: '', dueDate: '' })
      await load()
    } catch (e) {
      fail(e)
    }
  }

  async function markRenewed(r: Renewal) {
    setError(null)
    try {
      const updated = await api.post<Renewal>(`/api/renewals/${r.id}/renewed`, {})
      setMessage(updated.status === 'Active'
        ? `'${r.name}' renewed — next due ${new Date(updated.dueDate).toLocaleDateString()}.`
        : `'${r.name}' completed (one-time obligation closed).`)
      await load()
    } catch (e) {
      fail(e)
    }
  }

  async function cancel(r: Renewal) {
    try {
      await api.post(`/api/renewals/${r.id}/cancel`, {})
      await load()
    } catch (e) {
      fail(e)
    }
  }

  async function remove(id: string) {
    try {
      await api.delete(`/api/renewals/${id}`)
      await load()
    } catch (e) {
      fail(e)
    }
  }

  const visible = renewals.filter((r) => showClosed || r.status === 'Active')

  return (
    <div>
      {message && <div className="banner banner-ok">{message}</div>}
      {error && <div className="banner banner-error">{error}</div>}

      <section className="panel">
        <h2>Renewals &amp; commitments <span className="state state-staged">SETUP</span></h2>
        <p className="muted small">
          Anything that lapses if nobody acts — a professional license, an insurance policy, a
          loan installment, a lease, a corporate registration. The kernel watches the calendar:
          alerts escalate from Info to Critical as the due date approaches (default 90/30/7 days),
          a renewal task opens automatically, and an overdue commitment raises a Critical alarm.
          Marking a recurring commitment renewed advances it a full cycle.
        </p>

        <table>
          <thead>
            <tr><th>Commitment</th><th>Category</th><th>Counterparty / ref</th><th>Due</th><th>Status</th><th>Cycle</th><th>Notifies</th><th></th></tr>
          </thead>
          <tbody>
            {visible.map((r) => (
              <tr key={r.id}>
                <td><strong>{r.name}</strong></td>
                <td>{r.category}</td>
                <td className="small">{r.counterparty ?? '—'}{r.reference ? <span className="muted mono"> · {r.reference}</span> : null}</td>
                <td>{new Date(r.dueDate).toLocaleDateString()} {dueBadge(r)}</td>
                <td>{r.status}{r.renewalCount > 0 ? <span className="muted small"> ×{r.renewalCount}</span> : null}</td>
                <td className="small">{r.recurrenceMonths ? `every ${r.recurrenceMonths} mo` : 'one-time'}</td>
                <td className="small">{r.notifyRole}{r.createTask ? ' + task' : ''}</td>
                <td>
                  {r.status === 'Active' && (
                    <>
                      <button onClick={() => markRenewed(r)}>Renewed</button>{' '}
                      <button className="secondary" onClick={() => cancel(r)}>Cancel</button>{' '}
                    </>
                  )}
                  <button className="secondary" onClick={() => remove(r.id)}>Delete</button>
                </td>
              </tr>
            ))}
            {visible.length === 0 && (
              <tr><td colSpan={8} className="muted">
                No commitments watched yet. Add the first one below — a business license, an
                insurance policy, a loan due date — and the kernel will never let it sneak up.
              </td></tr>
            )}
          </tbody>
        </table>
        <label className="small muted">
          <input type="checkbox" checked={showClosed} onChange={(e) => setShowClosed(e.target.checked)} />{' '}
          show renewed / cancelled
        </label>

        <h3>Watch a commitment</h3>
        <div className="form-row">
          <label>Name<input placeholder="GL insurance policy" value={form.name}
            onChange={(e) => setForm({ ...form, name: e.target.value })} /></label>
          <label>Category<select value={form.category} onChange={(e) => setForm({ ...form, category: e.target.value })}>
            {CATEGORIES.map((c) => <option key={c} value={c}>{c}</option>)}
          </select></label>
          <label>Counterparty<input placeholder="insurer / lender / state board" value={form.counterparty}
            onChange={(e) => setForm({ ...form, counterparty: e.target.value })} /></label>
          <label>Reference<input placeholder="policy / license #" value={form.reference}
            onChange={(e) => setForm({ ...form, reference: e.target.value })} /></label>
          <label>Due date<input type="date" value={form.dueDate}
            onChange={(e) => setForm({ ...form, dueDate: e.target.value })} /></label>
          <label>Repeats every<input type="number" min="0" placeholder="months (blank = one-time)" value={form.recurrenceMonths}
            onChange={(e) => setForm({ ...form, recurrenceMonths: e.target.value })} style={{ width: 90 }} /></label>
          <label>Warn (days before)<input value={form.leadTimeDays}
            onChange={(e) => setForm({ ...form, leadTimeDays: e.target.value })} style={{ width: 90 }} /></label>
          <label>Notify<select value={form.notifyRole} onChange={(e) => setForm({ ...form, notifyRole: e.target.value })}>
            {ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
          </select></label>
          <label className="small">
            <input type="checkbox" checked={form.createTask}
              onChange={(e) => setForm({ ...form, createTask: e.target.checked })} /> open a task
          </label>
          <button onClick={create} disabled={!form.name.trim() || !form.dueDate}>Watch</button>
        </div>
      </section>
    </div>
  )
}
