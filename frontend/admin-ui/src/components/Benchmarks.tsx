import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api'

interface Benchmark {
  id: string
  name: string
  description: string | null
  kind: string
  metricType: string
  subjectId: string | null
  recordType: string | null
  field: string | null
  expectationOperator: string
  expectedValue: number
  severity: string
  notifyRole: string
  createTask: boolean
  enabled: boolean
}

const OPERATORS = ['GreaterOrEqual', 'LessOrEqual', 'GreaterThan', 'LessThan', 'Equals', 'NotEquals']
const ROLES = ['ExecutiveOwner', 'RiskComplianceOfficer', 'TreasuryOfficer', 'PlatformAdministrator', 'Auditor']

/// Tenant expectations with triggers: state what must hold (GP% >= 25%,
/// amount <= 10,000, violations <= 10) and who gets alarmed when it breaks.
export function Benchmarks({ tenant, user }: { tenant: string; user: string }) {
  const [benchmarks, setBenchmarks] = useState<Benchmark[]>([])
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [form, setForm] = useState({
    name: '', kind: 'score', metricType: '', recordType: '', field: '',
    expectationOperator: 'LessOrEqual', expectedValue: '', severity: 'Warning',
    notifyRole: 'ExecutiveOwner', createTask: true,
  })

  const fail = (e: unknown) => setError(e instanceof ApiError ? e.message : String(e))

  const load = useCallback(async () => {
    try {
      setBenchmarks(await api.get<Benchmark[]>('/api/benchmarks'))
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
      await api.post('/api/benchmarks', {
        name: form.name,
        description: null,
        kind: form.kind,
        metricType: form.kind === 'score' ? form.metricType : null,
        subjectId: null,
        recordType: form.kind === 'recordField' ? form.recordType : null,
        field: form.kind === 'recordField' ? form.field : null,
        expectationOperator: form.expectationOperator,
        expectedValue: Number(form.expectedValue),
        severity: form.severity,
        notifyRole: form.notifyRole,
        createTask: form.createTask,
        enabled: true,
      })
      setMessage(`Benchmark '${form.name}' active — breaches will raise ${form.severity} alerts to ${form.notifyRole}.`)
      setForm({ ...form, name: '', metricType: '', recordType: '', field: '', expectedValue: '' })
      await load()
    } catch (e) {
      fail(e)
    }
  }

  async function toggle(b: Benchmark) {
    try {
      await api.put(`/api/benchmarks/${b.id}`, { ...b, enabled: !b.enabled })
      await load()
    } catch (e) {
      fail(e)
    }
  }

  async function remove(id: string) {
    try {
      await api.delete(`/api/benchmarks/${id}`)
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
        <h2>Benchmarks &amp; expectations <span className="state state-staged">SETUP</span></h2>
        <p className="muted small">
          State what must hold — "GP% at or above 25%", "no movement above 10,000", "open
          violations at most 10" — and how the alarm fires. A breach raises a deviation alert (on
          the Dashboard), notifies the chosen role, and can open a review task. Score benchmarks
          watch every computed score/KPI; record benchmarks watch incoming payload fields.
        </p>

        <table>
          <thead>
            <tr><th>Expectation</th><th>Watches</th><th>Must hold</th><th>Severity</th><th>Notifies</th><th>Enabled</th><th></th></tr>
          </thead>
          <tbody>
            {benchmarks.map((b) => (
              <tr key={b.id}>
                <td><strong>{b.name}</strong></td>
                <td className="mono small">
                  {b.kind === 'score' ? `score:${b.metricType}` : `${b.recordType}.${b.field}`}
                </td>
                <td>{b.expectationOperator} {b.expectedValue}</td>
                <td><span className={`state state-${b.severity.toLowerCase()}`}>{b.severity}</span></td>
                <td>{b.notifyRole}{b.createTask ? ' + task' : ''}</td>
                <td>{b.enabled ? 'Yes' : 'No'}</td>
                <td>
                  <button onClick={() => toggle(b)}>{b.enabled ? 'Disable' : 'Enable'}</button>{' '}
                  <button onClick={() => remove(b.id)}>Delete</button>
                </td>
              </tr>
            ))}
            {benchmarks.length === 0 && <tr><td colSpan={7} className="muted">No expectations set yet.</td></tr>}
          </tbody>
        </table>

        <h3>Set an expectation</h3>
        <div className="form-row">
          <label>Name<input placeholder="GP% must stay above 25%" value={form.name}
            onChange={(e) => setForm({ ...form, name: e.target.value })} /></label>
          <label>Watch<select value={form.kind} onChange={(e) => setForm({ ...form, kind: e.target.value })}>
            <option value="score">a score / KPI</option>
            <option value="recordField">a record field</option>
          </select></label>
          {form.kind === 'score' ? (
            <label>Score type<input placeholder="e.g. GPPercent, AMLRisk, KPIActual" value={form.metricType}
              onChange={(e) => setForm({ ...form, metricType: e.target.value })} /></label>
          ) : (
            <>
              <label>Record type<input placeholder="e.g. withdrawal" value={form.recordType}
                onChange={(e) => setForm({ ...form, recordType: e.target.value })} /></label>
              <label>Field<input placeholder="e.g. amount" value={form.field}
                onChange={(e) => setForm({ ...form, field: e.target.value })} /></label>
            </>
          )}
          <label>Must be<select value={form.expectationOperator}
            onChange={(e) => setForm({ ...form, expectationOperator: e.target.value })}>
            {OPERATORS.map((o) => <option key={o} value={o}>{o}</option>)}
          </select></label>
          <label>Value<input type="number" step="any" value={form.expectedValue}
            onChange={(e) => setForm({ ...form, expectedValue: e.target.value })} /></label>
          <label>Severity<select value={form.severity} onChange={(e) => setForm({ ...form, severity: e.target.value })}>
            <option>Info</option><option>Warning</option><option>Critical</option>
          </select></label>
          <label>Notify<select value={form.notifyRole} onChange={(e) => setForm({ ...form, notifyRole: e.target.value })}>
            {ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
          </select></label>
          <label className="small">
            <input type="checkbox" checked={form.createTask}
              onChange={(e) => setForm({ ...form, createTask: e.target.checked })} /> open a task
          </label>
          <button onClick={create} disabled={!form.name.trim() || !form.expectedValue.trim()}>Activate</button>
        </div>
      </section>
    </div>
  )
}
