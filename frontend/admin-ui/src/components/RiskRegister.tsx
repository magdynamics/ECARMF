import { useCallback, useEffect, useState } from 'react'
import { SkeletonRows } from './SkeletonRows'
import { api } from '../api'
import type { ScoreRecord } from '../types'
import { RiskHeatmap, type RiskPoint } from './RiskHeatmap'

interface Props {
  tenant: string
  user: string
}

const clamp = (n: number) => Math.max(1, Math.min(5, Math.round(n)))

// A risk score's subjectId looks like "<kpiId>@<riskId>"; take the risk id.
function riskId(subjectId: string): string {
  const at = subjectId.lastIndexOf('@')
  return at >= 0 ? subjectId.slice(at + 1) : subjectId
}

// Prefer the severity/likelihood the KPI stamped into metadata; otherwise
// factor them out of the index so the point still lands on the grid.
function toPoint(s: ScoreRecord): RiskPoint {
  const sevMeta = Number(s.metadata?.severityValue ?? s.metadata?.severity)
  const likeMeta = Number(s.metadata?.likelihood)
  let severity: number
  let likelihood: number
  if (Number.isFinite(sevMeta) && Number.isFinite(likeMeta) && sevMeta > 0 && likeMeta > 0) {
    severity = clamp(sevMeta)
    likelihood = clamp(likeMeta)
  } else {
    severity = clamp(Math.sqrt(Math.max(1, s.value)))
    likelihood = clamp(s.value / Math.max(1, severity))
  }
  return {
    id: riskId(s.subjectId),
    label: s.riskType ?? riskId(s.subjectId),
    severity,
    likelihood,
    group: s.riskType ?? undefined,
    score: Number(s.value),
  }
}

interface OrgUnit { unitId: string; name: string; unitType: string; status: string }

export function RiskRegister({ tenant, user }: Props) {
  const [scores, setScores] = useState<ScoreRecord[] | null>(null)
  const [units, setUnits] = useState<OrgUnit[]>([])
  const [unitRef, setUnitRef] = useState('')
  const [error, setError] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    setError(null)
    try {
      const unitQ = unitRef ? `&unitRef=${encodeURIComponent(unitRef)}` : ''
      const all = await api.get<ScoreRecord[]>(`/api/scores?riskOnly=true&limit=3000${unitQ}`)
      try { setUnits((await api.get<OrgUnit[]>('/api/org-units')).filter((u) => u.status !== 'Archived')) } catch { setUnits([]) }
      // The register: the latest risk-tagged score per risk subject.
      const byRisk = new Map<string, ScoreRecord>()
      for (const s of all) {
        if (!s.riskType) continue
        const key = s.subjectId
        const prev = byRisk.get(key)
        if (!prev || new Date(s.computedAt) > new Date(prev.computedAt)) byRisk.set(key, s)
      }
      setScores([...byRisk.values()])
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load scores')
      setScores([])
    }
  }, [unitRef])

  useEffect(() => { void refresh() }, [refresh, tenant, user])

  const points = (scores ?? []).map(toPoint)
  const byDomain = new Map<string, RiskPoint[]>()
  for (const p of points) {
    const g = p.group ?? 'Untagged'
    ;(byDomain.get(g) ?? byDomain.set(g, []).get(g)!).push(p)
  }
  const domains = [...byDomain.entries()].sort((a, b) => b[1].length - a[1].length)
  const critical = points.filter((p) => p.severity * p.likelihood >= 15).length

  return (
    <div>
      <section className="panel">
        <h2>Risk Register</h2>
        <p className="muted">
          Every risk-tagged score for tenant <strong>{tenant}</strong>, plotted by severity and
          likelihood. Each risk domain (<code>riskType</code>) is a colour-independent band; the
          cell position and its shape marker carry the severity so the map is readable without
          relying on colour. Click a cell to list the risks in it.
        </p>
        {units.length > 0 && (
          <div className="form-row">
            <label>Entity / location
              <select value={unitRef} onChange={(e) => setUnitRef(e.target.value)}>
                <option value="">All units</option>
                {units.map((u) => <option key={u.unitId} value={u.unitId}>{u.name} (+ tenant-wide)</option>)}
              </select>
            </label>
          </div>
        )}
        {points.length > 0 && (
          <p className="small">
            <strong>{points.length}</strong> risk(s) across <strong>{domains.length}</strong> domain(s)
            {critical > 0 && <> · <span className="posture-chip posture-regulated">{critical} in the Critical zone</span></>}
          </p>
        )}
        {error && <p className="error small">{error}</p>}
      </section>

      {scores === null ? (
        <section className="panel"><SkeletonRows /></section>
      ) : points.length === 0 ? (
        <section className="panel">
          <h3>No risks scored yet</h3>
          <p className="muted">
            The heatmap fills as risk-tagged scores arrive. Submit assessment records (e.g. a
            <code> T10RiskAssessment</code> for the RCM tenant, or any KPI carrying a <code>riskType</code>)
            and they appear here plotted by severity × likelihood.
          </p>
        </section>
      ) : (
        <>
          <section className="panel">
            <RiskHeatmap risks={points} />
          </section>
          <section className="panel">
            <h3>By domain</h3>
            <div className="domain-grid">
              {domains.map(([g, list]) => {
                const worst = Math.max(...list.map((p) => p.severity * p.likelihood))
                return (
                  <div key={g} className="domain-tile">
                    <strong className="mono">{g}</strong>
                    <span className="muted small">{list.length} risk(s) · worst {worst}</span>
                  </div>
                )
              })}
            </div>
          </section>
        </>
      )}
    </div>
  )
}
