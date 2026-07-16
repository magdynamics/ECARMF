import { useCallback, useEffect, useState } from 'react'
import { SkeletonRows } from './SkeletonRows'
import { Icon } from './Icon'
import { Donut, DonutLegend } from './charts'
import { api } from '../api'
import type { ScoreRecord } from '../types'

// Executive board pack — a per-tenant one-page brief assembled from what the
// platform already computes: risk posture, treatment progress, this-period-vs-
// last, top cases, and upcoming renewals. Printable (browser print → PDF).

interface Pack {
  brand: string; segment?: string | null; posture: string
  risks: number; critical: number; topDomains: [string, number][]
  treated: number; byStatus: Record<string, number>
  period?: { current: string; previous: string; deltas: { metric: string; current: number; changePct: number; improved: boolean }[]; recs: string[] }
  cases: { name: string; records: number; critical: number }[]
  renewals: { name: string; category: string; days: number }[]
}

export function BoardPack({ tenant, user }: { tenant: string; user: string }) {
  const [pack, setPack] = useState<Pack | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    setError(null); setPack(null)
    try {
      const [cfg, risk, treatments, periods, cases, renewals] = await Promise.all([
        api.get<{ brand: string; segment?: string | null; posture: string }>('/api/tenant-config').catch(() => ({ brand: tenant, segment: null as string | null, posture: 'standard' })),
        api.get<ScoreRecord[]>('/api/scores?riskOnly=true&limit=3000').catch(() => []),
        api.get<{ status: string }[]>('/api/risk/treatments').catch(() => []),
        api.get<{ comparison: { current?: { label: string } | null; previous?: { label: string } | null; deltas: { metric: string; current: number; changePct: number; improved: boolean }[]; recommendations: string[] } }>('/api/analysis/periods?granularity=month&count=6').catch(() => null),
        api.get<{ name: string; records: number; rejected: number }[]>('/api/cases').catch(() => []),
        api.get<{ name: string; category: string; dueDate: string }[]>('/api/renewals').catch(() => []),
      ])

      // Risk: one per subject (latest), by domain.
      const seen = new Set<string>()
      const domains = new Map<string, number>()
      let critical = 0
      for (const s of risk) {
        const subj = s.subjectId ?? ''
        if (!subj || seen.has(subj)) continue
        seen.add(subj)
        const d = s.riskType ?? 'General'
        domains.set(d, (domains.get(d) ?? 0) + 1)
        const sev = Number(s.metadata?.severityValue ?? s.metadata?.severity) || 0
        const like = Number(s.metadata?.likelihood) || 0
        if (sev >= 4 && like >= 4) critical++
      }
      const byStatus: Record<string, number> = {}
      for (const t of treatments) byStatus[t.status] = (byStatus[t.status] ?? 0) + 1

      const now = Date.now()
      const c = periods?.comparison
      setPack({
        brand: cfg.brand, segment: cfg.segment, posture: cfg.posture,
        risks: seen.size, critical,
        topDomains: [...domains.entries()].sort((a, b) => b[1] - a[1]).slice(0, 5),
        treated: treatments.length, byStatus,
        period: c && c.current && c.previous ? {
          current: c.current.label, previous: c.previous.label,
          deltas: c.deltas, recs: c.recommendations,
        } : undefined,
        cases: cases.map((x) => ({ name: x.name, records: x.records, critical: x.rejected })).sort((a, b) => b.records - a.records).slice(0, 5),
        renewals: renewals.map((r) => ({ name: r.name, category: r.category, days: Math.ceil((new Date(r.dueDate).getTime() - now) / 86400000) }))
          .filter((r) => r.days <= 90).sort((a, b) => a.days - b.days).slice(0, 6),
      })
    } catch (e) { setError(e instanceof Error ? e.message : String(e)) }
  }, [tenant])
  useEffect(() => { void load() }, [load, user])

  if (error) return <section className="panel"><p className="error small">{error}</p></section>
  if (!pack) return <section className="panel"><SkeletonRows rows={5} /></section>

  return (
    <div className="boardpack">
      <section className="panel">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', flexWrap: 'wrap', gap: '0.5rem' }}>
          <div>
            <h2>{pack.brand} — Board Pack</h2>
            <p className="muted small">{pack.segment ?? tenant} · {pack.posture} posture · generated {new Date().toLocaleDateString()}</p>
          </div>
          <button className="secondary no-print" onClick={() => window.print()}>Print / export PDF</button>
        </div>
      </section>

      <section className="panel">
        <h3>Risk posture</h3>
        <p className="small">
          <strong>{pack.risks}</strong> tracked risks · <strong className="error-text">{pack.critical}</strong> in the critical zone ·{' '}
          <strong>{pack.treated}</strong> under treatment
        </p>
        {Object.keys(pack.byStatus).length > 0 && (
          <div className="chart-row">
            <Donut data={Object.entries(pack.byStatus).map(([label, value]) => ({ label, value }))} size={96} thickness={11} />
            <DonutLegend data={Object.entries(pack.byStatus).map(([label, value]) => ({ label, value }))} />
          </div>
        )}
        {pack.topDomains.length > 0 && (
          <p className="muted small">Top domains: {pack.topDomains.map(([d, n]) => `${d} (${n})`).join(' · ')}</p>
        )}
      </section>

      {pack.period && (
        <section className="panel">
          <h3>Trend — {pack.period.current} vs {pack.period.previous}</h3>
          <div className="pd-deltas">
            {pack.period.deltas.map((d) => (
              <div key={d.metric} className={`pd-delta ${d.improved ? 'good' : 'bad'}`}>
                <span className="muted small">{d.metric}</span>
                <strong>{d.current.toLocaleString()}</strong>
                <span className="pd-change">{d.changePct > 0 ? '▲' : d.changePct < 0 ? '▼' : '■'} {Math.abs(d.changePct)}%</span>
              </div>
            ))}
          </div>
          {pack.period.recs.map((r, i) => <p key={i} className="pd-rec"><Icon name="lightbulb" size={13} /> {r}</p>)}
        </section>
      )}

      {pack.cases.length > 0 && (
        <section className="panel">
          <h3>Cases</h3>
          <table className="pd-table"><thead><tr><th>Case</th><th>Records</th><th>Rejected</th></tr></thead>
            <tbody>{pack.cases.map((c) => <tr key={c.name}><td>{c.name}</td><td>{c.records}</td><td className={c.critical > 0 ? 'error-text' : 'muted'}>{c.critical}</td></tr>)}</tbody>
          </table>
        </section>
      )}

      {pack.renewals.length > 0 && (
        <section className="panel">
          <h3>Upcoming renewals</h3>
          <table className="pd-table"><thead><tr><th>Renewal</th><th>Category</th><th>Due</th></tr></thead>
            <tbody>{pack.renewals.map((r, i) => <tr key={i}><td>{r.name}</td><td className="muted small">{r.category}</td><td className={r.days < 0 ? 'error-text' : r.days <= 14 ? '' : 'muted'}>{r.days < 0 ? `overdue ${-r.days}d` : `${r.days}d`}</td></tr>)}</tbody>
          </table>
        </section>
      )}
    </div>
  )
}
