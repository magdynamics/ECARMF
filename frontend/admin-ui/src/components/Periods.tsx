import { useCallback, useEffect, useState } from 'react'
import { Icon } from './Icon'
import { api, ApiError } from '../api'

// Period analysis — "how are we doing this period versus last?". Buckets the
// tenant's records, outcomes, and KPI scores into calendar periods and turns
// the latest-vs-previous deltas into plain recommendations.

interface PeriodMetrics {
  label: string; start: string; end: string
  records: number; rejected: number; flagged: number; controlsFired: number; avgScore: number
}
interface PeriodDelta { metric: string; current: number; previous: number; changePct: number; improved: boolean }
interface PeriodComparison { current: PeriodMetrics | null; previous: PeriodMetrics | null; deltas: PeriodDelta[]; recommendations: string[] }
interface PeriodAnalysis { granularity: string; periods: PeriodMetrics[]; comparison: PeriodComparison }

export function Periods({ tenant, user }: { tenant: string; user: string }) {
  const [gran, setGran] = useState<'month' | 'quarter'>('month')
  const [data, setData] = useState<PeriodAnalysis | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    setError(null); setData(null)
    try {
      setData(await api.get<PeriodAnalysis>(`/api/analysis/periods?granularity=${gran}&count=6`))
    } catch (e) { setError(e instanceof ApiError ? e.message : String(e)) }
  }, [gran])
  useEffect(() => { void load() }, [load, tenant, user])

  const maxRecords = Math.max(1, ...(data?.periods ?? []).map((p) => p.records))
  const c = data?.comparison

  return (
    <div>
      <section className="panel">
        <h2>Period analysis</h2>
        <p className="muted">
          How this tenant is doing this {gran} versus last — records, control outcomes, and average
          risk score per period, with recommendations from the change.
        </p>
        <label>Granularity
          <select value={gran} onChange={(e) => setGran(e.target.value as 'month' | 'quarter')}>
            <option value="month">Monthly</option>
            <option value="quarter">Quarterly</option>
          </select>
        </label>
        {error && <p className="error small">{error}</p>}
      </section>

      {c && c.current && c.previous && (
        <section className="panel">
          <h3>{c.current.label} vs {c.previous.label}</h3>
          <div className="pd-deltas">
            {c.deltas.map((d) => (
              <div key={d.metric} className={`pd-delta ${d.improved ? 'good' : 'bad'}`}>
                <span className="muted small">{d.metric}</span>
                <strong>{d.current.toLocaleString()}</strong>
                <span className="pd-change">
                  {d.changePct > 0 ? '▲' : d.changePct < 0 ? '▼' : '■'} {Math.abs(d.changePct)}%
                  <span className="muted small"> vs {d.previous.toLocaleString()}</span>
                </span>
              </div>
            ))}
          </div>
          <div className="pd-recs">
            {c.recommendations.map((r, i) => <div key={i} className="pd-rec"><Icon name="lightbulb" size={13} /> {r}</div>)}
          </div>
        </section>
      )}

      <section className="panel">
        <h3>Periods</h3>
        {data === null ? <p className="muted">Loading…</p>
          : data.periods.every((p) => p.records === 0) ? <p className="muted">No records in this window yet.</p>
          : (
            <table className="pd-table">
              <thead><tr><th>Period</th><th>Records</th><th>Rejected</th><th>Flagged</th><th>Controls</th><th>Avg risk</th></tr></thead>
              <tbody>
                {data.periods.map((p) => (
                  <tr key={p.label}>
                    <td><strong>{p.label}</strong></td>
                    <td>
                      <div className="pd-bar-wrap">
                        <div className="pd-bar" style={{ width: `${(p.records / maxRecords) * 100}%` }} />
                        <span>{p.records}</span>
                      </div>
                    </td>
                    <td className={p.rejected > 0 ? 'error-text' : 'muted'}>{p.rejected}</td>
                    <td>{p.flagged}</td>
                    <td className="muted">{p.controlsFired}</td>
                    <td>{p.avgScore > 0 ? p.avgScore.toLocaleString() : '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
      </section>
    </div>
  )
}
