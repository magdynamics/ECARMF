import { useCallback, useEffect, useMemo, useState } from 'react'
import { api, ApiError } from '../api'
import { Donut, DonutLegend, MeterBar } from './charts'
import type { ActivityItem, AuditEntryDto, ScoreRecord } from '../types'

const POLL_MS = 5000

interface OrgUnit { unitId: string; name: string; unitType: string; status: string }

interface Widget {
  id: string
  type: string
  title: string
}

interface DashboardConfig {
  id: string
  name: string
  widgets: Widget[]
}

interface DeviationAlert {
  id: string
  entityReference: string
  metricType: string
  actualValue: number
  expectedValue: number
  expectedValueSource: string
  varianceMagnitude: number
  severity: string
  detectedAt: string
  acknowledgedBy: string | null
}

interface BenchmarkDto {
  id: string
  name: string
  kind: string
  metricType: string
  recordType: string | null
  field: string | null
  expectationOperator: string
  expectedValue: number
  severity: string
  enabled: boolean
}

interface IntegrationDto {
  integrationId: string
  name: string
  applicationType: string
  status: string
  lastFeedAt: string | null
  lastFeedStatus: string | null
}

interface TaskDto {
  id: string
  title: string
  assignee: string
  severity: string
  status: string
  createdAt: string
}

const WIDGET_TYPES = [
  'kpiTiles', 'outcomeBreakdown', 'scoreAverages', 'okrAttainment', 'deviationFeed', 'recentScores',
  'benchmarks', 'integrationHealth', 'taskInbox',
]

/// Widget-driven dashboard: the layout is live, editable configuration
/// (DashboardDefinition) — add/remove widgets without a package rebuild.
/// Every widget renders purely from ScoreRecord/AuditLog/record queries.
export function Dashboard({ tenant, user }: { tenant: string; user: string }) {
  const [config, setConfig] = useState<DashboardConfig | null>(null)
  const [scores, setScores] = useState<ScoreRecord[]>([])
  const [records, setRecords] = useState<ActivityItem[]>([])
  const [audit, setAudit] = useState<AuditEntryDto[]>([])
  const [deviations, setDeviations] = useState<DeviationAlert[]>([])
  const [benchmarks, setBenchmarks] = useState<BenchmarkDto[]>([])
  const [integrations, setIntegrations] = useState<IntegrationDto[]>([])
  const [tasks, setTasks] = useState<TaskDto[]>([])
  const [error, setError] = useState<string | null>(null)
  const [newType, setNewType] = useState('kpiTiles')
  const [units, setUnits] = useState<OrgUnit[]>([])
  const [unitRef, setUnitRef] = useState('')

  const refresh = useCallback(async () => {
    try {
      const unitQ = unitRef ? `&unitRef=${encodeURIComponent(unitRef)}` : ''
      const [dashboards, s, r, a, d, b, t] = await Promise.all([
        api.get<DashboardConfig[]>('/api/dashboards'),
        api.get<ScoreRecord[]>(`/api/scores?limit=500${unitQ}`),
        api.get<ActivityItem[]>(`/api/records?limit=200${unitQ}`),
        api.get<AuditEntryDto[]>('/api/audit'),
        api.get<DeviationAlert[]>('/api/deviations?limit=25'),
        api.get<BenchmarkDto[]>('/api/benchmarks').catch(() => [] as BenchmarkDto[]),
        api.get<TaskDto[]>('/api/tasks?limit=15').catch(() => [] as TaskDto[]),
      ])
      setConfig(dashboards[0] ?? null)
      setScores(s)
      setRecords(r)
      setAudit(a)
      setDeviations(unitRef ? d.filter((x: DeviationAlert & { unitRef?: string | null }) => !('unitRef' in x) || x.unitRef === unitRef || x.unitRef == null) : d)
      setBenchmarks(b)
      setTasks(t)
      // integrations list needs configure permission; degrade silently.
      setIntegrations(await api.get<IntegrationDto[]>('/api/integrations').catch(() => [] as IntegrationDto[]))
      try { setUnits((await api.get<OrgUnit[]>('/api/org-units')).filter((u) => u.status !== 'Archived')) } catch { setUnits([]) }
      setError(null)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }, [unitRef])

  useEffect(() => {
    void refresh()
    const timer = setInterval(() => void refresh(), POLL_MS)
    return () => clearInterval(timer)
  }, [refresh, tenant, user])

  async function persist(widgets: Widget[]) {
    if (!config) return
    setConfig({ ...config, widgets })
    try {
      await api.put(`/api/dashboards/${config.id}/widgets`, { widgets })
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }

  function addWidget() {
    if (!config) return
    const widget: Widget = {
      id: `w${Date.now()}`,
      type: newType,
      title: newType.replace(/([A-Z])/g, ' $1').replace(/^./, (c) => c.toUpperCase()),
    }
    void persist([...config.widgets, widget])
  }

  function removeWidget(id: string) {
    if (!config) return
    void persist(config.widgets.filter((w) => w.id !== id))
  }

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
    const okrBySubject = new Map<string, ScoreRecord>()
    for (const s of scores.filter((x) => x.scoreType === 'OKRAttainment')) {
      const existing = okrBySubject.get(s.subjectId)
      if (!existing || existing.computedAt < s.computedAt) okrBySubject.set(s.subjectId, s)
    }
    const decided = records.filter((r) => latestOutcome(r) !== null)
    const flagged = records.filter((r) => r.outcomes.some((o) => o.outcome.toLowerCase() === 'flagged'))
    const overrides = audit.filter((a) => a.category === 'ApprovalRecorded')
    return {
      outcomeCounts: [...outcomeCounts.entries()].sort((a, b) => b[1] - a[1]),
      averages: [...avgByType.entries()]
        .map(([type, { sum, n }]) => ({ type, avg: sum / n, n }))
        .sort((a, b) => a.type.localeCompare(b.type)),
      okrAttainment: [...okrBySubject.values()].sort((a, b) => b.value - a.value),
      totalRecords: records.length,
      opportunityCount: records.filter((r) => r.recordType.toLowerCase() === 'opportunity').length,
      cycleCompletionRate: records.length ? decided.length / records.length : null,
      manualOverrideRate: flagged.length ? overrides.length / flagged.length : null,
      auditEntries24h: audit.length,
    }
  }, [records, scores, audit])

  const pct = (v: number | null) => (v === null ? '—' : `${(v * 100).toFixed(0)}%`)

  function renderWidget(widget: Widget) {
    switch (widget.type) {
      case 'kpiTiles':
        return (
          <div className="kpi-grid">
            {[
              [kpis.totalRecords, 'Records (last 200)'],
              [kpis.opportunityCount, 'Opportunities'],
              [pct(kpis.cycleCompletionRate), 'Cycle completion'],
              [pct(kpis.manualOverrideRate), 'Manual override (flagged)'],
              [kpis.auditEntries24h, 'Audit entries (24h)'],
            ].map(([value, label]) => (
              <div key={String(label)} className="panel kpi">
                <div className="kpi-value">{value as never}</div>
                <div className="muted small">{label}</div>
              </div>
            ))}
          </div>
        )
      case 'outcomeBreakdown': {
        const donutData = kpis.outcomeCounts.map(([label, value]) => ({ label, value }))
        return donutData.length === 0 ? (
          <p className="muted small">No outcomes yet.</p>
        ) : (
          <div className="chart-row">
            <Donut data={donutData} />
            <DonutLegend data={donutData} />
          </div>
        )
      }
      case 'scoreAverages': {
        const maxAvg = Math.max(1e-9, ...kpis.averages.map((r) => r.avg))
        return (
          <table>
            <tbody>
              {kpis.averages.map((row) => (
                <tr key={row.type}>
                  <td><code>{row.type}</code></td>
                  <td><MeterBar value={row.avg} max={maxAvg} />{row.avg.toFixed(3)}</td>
                  <td className="muted">{row.n} samples</td>
                </tr>
              ))}
            </tbody>
          </table>
        )
      }
      case 'okrAttainment':
        return kpis.okrAttainment.length === 0 ? (
          <p className="muted small">No OKR scores yet — ingest operational records.</p>
        ) : (
          <table>
            <tbody>
              {kpis.okrAttainment.map((s) => (
                <tr key={s.subjectId}>
                  <td><code>{s.subjectId}</code></td>
                  <td>
                    <MeterBar value={s.value} max={1} color={s.value >= 0.8 ? 'var(--ok-text)' : s.value >= 0.5 ? 'var(--warn-text)' : 'var(--bad-text)'} />
                    {(s.value * 100).toFixed(0)}%
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )
      case 'deviationFeed':
        return deviations.length === 0 ? (
          <p className="muted small">No deviations — actuals are within threshold of targets/forecasts.</p>
        ) : (
          <table>
            <tbody>
              {deviations.map((d) => (
                <tr key={d.id}>
                  <td><span className={`state state-${d.severity.toLowerCase() === 'critical' ? 'rejected' : 'flagged'}`}>{d.severity}</span></td>
                  <td className="small"><code>{d.entityReference}</code></td>
                  <td className="small">
                    actual {d.actualValue} vs {d.expectedValueSource.toLowerCase()} {d.expectedValue} ({(d.varianceMagnitude * 100).toFixed(0)}%)
                  </td>
                  <td className="muted small">{d.acknowledgedBy ? `ack: ${d.acknowledgedBy}` : new Date(d.detectedAt).toLocaleTimeString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )
      case 'recentScores':
        return (
          <table>
            <tbody>
              {scores.slice(0, 10).map((s) => (
                <tr key={s.id}>
                  <td><code>{s.scoreType}</code></td>
                  <td className="small">{s.subjectType} <span className="muted">{s.subjectId}</span></td>
                  <td>{s.value}</td>
                  <td className="muted small">{s.ruleId} ({s.packageId} v{s.packageVersion})</td>
                </tr>
              ))}
            </tbody>
          </table>
        )
      case 'benchmarks': {
        const breaches = deviations.filter((d) => d.expectedValueSource === 'Benchmark')
        return benchmarks.length === 0 ? (
          <p className="muted small">No expectations set — define them under Setup → Benchmarks.</p>
        ) : (
          <table>
            <tbody>
              {benchmarks.map((b) => {
                const breachCount = breaches.filter((x) =>
                  x.metricType === (b.kind === 'score' ? b.metricType : `${b.recordType}.${b.field}`)).length
                return (
                  <tr key={b.id}>
                    <td><strong>{b.name}</strong></td>
                    <td className="small">{b.expectationOperator} {b.expectedValue}</td>
                    <td>{b.enabled ? <span className="state state-active">watching</span> : <span className="state state-deactivated">off</span>}</td>
                    <td>
                      {breachCount > 0
                        ? <span className={`state state-${b.severity.toLowerCase() === 'critical' ? 'rejected' : 'flagged'}`}>{breachCount} breach(es)</span>
                        : <span className="state state-approved">holding</span>}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        )
      }
      case 'integrationHealth':
        return integrations.length === 0 ? (
          <p className="muted small">No integrations configured — add them under Setup → Integrations.</p>
        ) : (
          <table>
            <tbody>
              {integrations.map((i) => (
                <tr key={i.integrationId}>
                  <td><strong>{i.name}</strong> <span className="muted small">{i.applicationType}</span></td>
                  <td><span className={`state state-${i.status.toLowerCase()}`}>{i.status}</span></td>
                  <td className="small">
                    {i.lastFeedAt
                      ? <>last feed {new Date(i.lastFeedAt).toLocaleString()} — {i.lastFeedStatus === 'Succeeded'
                          ? <span className="state state-approved">ok</span>
                          : <span className="state state-rejected">failed</span>}</>
                      : <span className="muted">no feeds yet</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )
      case 'taskInbox': {
        const open = tasks.filter((t) => t.status === 'Open')
        return open.length === 0 ? (
          <p className="muted small">No open tasks — workflows and benchmarks will queue work here.</p>
        ) : (
          <table>
            <tbody>
              {open.map((t) => (
                <tr key={t.id}>
                  <td><span className={`state state-${t.severity.toLowerCase() === 'critical' ? 'rejected' : 'flagged'}`}>{t.severity}</span></td>
                  <td>{t.title}</td>
                  <td className="muted small">{t.assignee} · {new Date(t.createdAt).toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )
      }
      default:
        return <p className="muted small">Unknown widget type '{widget.type}'.</p>
    }
  }

  return (
    <div>
      {error && <div className="banner banner-error">{error}</div>}

      <section className="panel">
        <h2>{config?.name ?? 'Dashboard'} <span className="state state-approved">OUTPUT</span></h2>
        <p className="muted small">
          This dashboard is live configuration, not code: add or remove widgets below and the
          layout is saved for this tenant instantly — no package rebuild involved.
        </p>
        <div className="form-row">
          {units.length > 0 && (
            <label>
              Entity / location
              <select value={unitRef} onChange={(e) => setUnitRef(e.target.value)}
                title="Narrow every widget to one unit. A unit's view includes tenant-wide figures (they apply to all units).">
                <option value="">All units</option>
                {units.map((u) => <option key={u.unitId} value={u.unitId}>{u.name} (+ tenant-wide)</option>)}
              </select>
            </label>
          )}
          <label>
            Widget type
            <select value={newType} onChange={(e) => setNewType(e.target.value)}>
              {WIDGET_TYPES.map((t) => (
                <option key={t} value={t}>{t}</option>
              ))}
            </select>
          </label>
          <button onClick={addWidget} disabled={!config}>Add widget</button>
        </div>
        {unitRef && (
          <p className="muted small">
            Scoped to <strong>{units.find((u) => u.unitId === unitRef)?.name ?? unitRef}</strong> —
            showing that unit's records and scores plus tenant-wide ones.
          </p>
        )}
      </section>

      {config?.widgets.map((widget) => (
        <section key={widget.id} className="panel">
          <div className="step-head" style={{ justifyContent: 'space-between' }}>
            <h2 style={{ margin: 0 }}>{widget.title}</h2>
            <button className="secondary" onClick={() => removeWidget(widget.id)}>Remove</button>
          </div>
          {renderWidget(widget)}
        </section>
      ))}
    </div>
  )
}
