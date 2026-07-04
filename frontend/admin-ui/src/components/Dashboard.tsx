import { useCallback, useEffect, useMemo, useState } from 'react'
import { api, ApiError } from '../api'
import type { ActivityItem, AuditEntryDto, ScoreRecord } from '../types'

const POLL_MS = 5000

/// KPIs computed purely from ScoreRecord / AuditLog / record-feed queries —
/// the dashboard introduces no new backend concepts.
export function Dashboard({ tenant, user }: { tenant: string; user: string }) {
  const [scores, setScores] = useState<ScoreRecord[]>([])
  const [records, setRecords] = useState<ActivityItem[]>([])
  const [audit, setAudit] = useState<AuditEntryDto[]>([])
  const [error, setError] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    try {
      const [s, r, a] = await Promise.all([
        api.get<ScoreRecord[]>('/api/scores?limit=500'),
        api.get<ActivityItem[]>('/api/records?limit=200'),
        api.get<AuditEntryDto[]>('/api/audit'),
      ])
      setScores(s)
      setRecords(r)
      setAudit(a)
      setError(null)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }, [])

  useEffect(() => {
    void refresh()
    const timer = setInterval(() => void refresh(), POLL_MS)
    return () => clearInterval(timer)
  }, [refresh, tenant, user])

  const kpis = useMemo(() => {
    const latestOutcome = (r: ActivityItem) => r.outcomes[r.outcomes.length - 1]?.outcome ?? null

    const outcomeCounts = new Map<string, number>()
    for (const r of records) {
      const o = latestOutcome(r) ?? 'Pending'
      outcomeCounts.set(o, (outcomeCounts.get(o) ?? 0) + 1)
    }

    const avgByType = new Map<string, { sum: number; n: number }>()
    for (const s of scores) {
      const acc = avgByType.get(s.scoreType) ?? { sum: 0, n: 0 }
      acc.sum += s.value
      acc.n += 1
      avgByType.set(s.scoreType, acc)
    }

    // Trust movement: oldest vs newest average across trust scores.
    const trust = scores
      .filter((s) => s.scoreType === 'Trust')
      .sort((a, b) => a.computedAt.localeCompare(b.computedAt))
    const half = Math.floor(trust.length / 2)
    const avg = (list: ScoreRecord[]) =>
      list.length ? list.reduce((t, s) => t + s.value, 0) / list.length : null
    const trustEarly = avg(trust.slice(0, half))
    const trustLate = avg(trust.slice(half))

    const opportunities = records.filter((r) => r.recordType.toLowerCase() === 'opportunity')
    const decided = records.filter((r) => latestOutcome(r) !== null)
    const flagged = records.filter((r) =>
      r.outcomes.some((o) => o.outcome.toLowerCase() === 'flagged'),
    )
    const overrides = audit.filter((a) => a.category === 'ApprovalRecorded')
    const rejected = records.filter((r) => latestOutcome(r)?.toLowerCase() === 'rejected')

    // OKR attainment by venture/site: latest OKRAttainment per subject.
    const okrBySubject = new Map<string, ScoreRecord>()
    for (const s of scores.filter((x) => x.scoreType === 'OKRAttainment')) {
      const existing = okrBySubject.get(s.subjectId)
      if (!existing || existing.computedAt < s.computedAt) okrBySubject.set(s.subjectId, s)
    }

    return {
      okrAttainment: [...okrBySubject.values()].sort((a, b) => b.value - a.value),
      totalRecords: records.length,
      opportunityCount: opportunities.length,
      outcomeCounts: [...outcomeCounts.entries()].sort((a, b) => b[1] - a[1]),
      averages: [...avgByType.entries()]
        .map(([type, { sum, n }]) => ({ type, avg: sum / n, n }))
        .sort((a, b) => a.type.localeCompare(b.type)),
      trustEarly,
      trustLate,
      cycleCompletionRate: records.length ? decided.length / records.length : null,
      manualOverrideRate: flagged.length ? overrides.length / flagged.length : null,
      auditExceptionRate: records.length ? rejected.length / records.length : null,
      auditEntries24h: audit.length,
    }
  }, [records, scores, audit])

  const pct = (v: number | null) => (v === null ? '—' : `${(v * 100).toFixed(0)}%`)
  const num = (v: number | null, digits = 2) => (v === null ? '—' : v.toFixed(digits))

  return (
    <div>
      {error && <div className="banner banner-error">{error}</div>}

      <div className="kpi-grid">
        <div className="panel kpi">
          <div className="kpi-value">{kpis.totalRecords}</div>
          <div className="muted small">Records (last 200)</div>
        </div>
        <div className="panel kpi">
          <div className="kpi-value">{kpis.opportunityCount}</div>
          <div className="muted small">Opportunities</div>
        </div>
        <div className="panel kpi">
          <div className="kpi-value">{pct(kpis.cycleCompletionRate)}</div>
          <div className="muted small">Cycle completion rate</div>
        </div>
        <div className="panel kpi">
          <div className="kpi-value">{pct(kpis.manualOverrideRate)}</div>
          <div className="muted small">Manual override rate (flagged)</div>
        </div>
        <div className="panel kpi">
          <div className="kpi-value">{pct(kpis.auditExceptionRate)}</div>
          <div className="muted small">Audit exception (rejected) rate</div>
        </div>
        <div className="panel kpi">
          <div className="kpi-value">{kpis.auditEntries24h}</div>
          <div className="muted small">Audit entries (24h)</div>
        </div>
        <div className="panel kpi">
          <div className="kpi-value">
            {num(kpis.trustEarly)} → {num(kpis.trustLate)}
          </div>
          <div className="muted small">Trust score movement</div>
        </div>
      </div>

      <div className="two-col">
        <section className="panel">
          <h2>Outcome breakdown</h2>
          <table>
            <thead>
              <tr>
                <th>Outcome</th>
                <th>Count</th>
              </tr>
            </thead>
            <tbody>
              {kpis.outcomeCounts.length === 0 && (
                <tr>
                  <td colSpan={2} className="muted">No records yet.</td>
                </tr>
              )}
              {kpis.outcomeCounts.map(([outcome, count]) => (
                <tr key={outcome}>
                  <td>
                    <span className={`state state-${outcome.toLowerCase()}`}>{outcome}</span>
                  </td>
                  <td>{count}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>

        <section className="panel">
          <h2>Average score by type</h2>
          <table>
            <thead>
              <tr>
                <th>Score type</th>
                <th>Average</th>
                <th>Samples</th>
              </tr>
            </thead>
            <tbody>
              {kpis.averages.length === 0 && (
                <tr>
                  <td colSpan={3} className="muted">
                    No scores yet — activate a scoring package (e.g. Flywheel
                    Opportunity Evaluation) and submit records.
                  </td>
                </tr>
              )}
              {kpis.averages.map((row) => (
                <tr key={row.type}>
                  <td><code>{row.type}</code></td>
                  <td>{row.avg.toFixed(3)}</td>
                  <td className="muted">{row.n}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      </div>

      <section className="panel">
        <h2>OKR attainment by venture</h2>
        <table>
          <thead>
            <tr>
              <th>OKR @ venture/site</th>
              <th>Attainment</th>
              <th>Computed</th>
            </tr>
          </thead>
          <tbody>
            {kpis.okrAttainment.length === 0 && (
              <tr>
                <td colSpan={3} className="muted">
                  No OKR scores yet — activate a performance framework package
                  and ingest operational records.
                </td>
              </tr>
            )}
            {kpis.okrAttainment.map((s) => (
              <tr key={s.subjectId}>
                <td><code>{s.subjectId}</code></td>
                <td>{(s.value * 100).toFixed(0)}%</td>
                <td className="small muted">{new Date(s.computedAt).toLocaleTimeString()}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      <section className="panel">
        <h2>Recent scores</h2>
        <table>
          <thead>
            <tr>
              <th>Computed</th>
              <th>Score</th>
              <th>Subject</th>
              <th>Value</th>
              <th>Computed by</th>
            </tr>
          </thead>
          <tbody>
            {scores.slice(0, 15).map((s) => (
              <tr key={s.id}>
                <td className="small">{new Date(s.computedAt).toLocaleTimeString()}</td>
                <td><code>{s.scoreType}</code></td>
                <td className="small">
                  {s.subjectType} <span className="muted">{s.subjectId}</span>
                </td>
                <td>{s.value}</td>
                <td className="muted small">
                  {s.ruleId} ({s.packageId} v{s.packageVersion})
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
    </div>
  )
}
