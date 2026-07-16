import { Fragment, useCallback, useEffect, useMemo, useState } from 'react'
import { SkeletonRows } from './SkeletonRows'
import { api, ApiError } from '../api'

// Audit explorer — the append-only trail this tenant records, made browsable.
// Every control decision, package change, config edit, and risk action lands
// here; this surfaces it with a time window, category filter, and text search.

interface AuditEntry {
  id: string; correlationId: string; category: string
  actor?: string; summary: string; detail?: Record<string, string>; occurredAt: string
}

const RANGES: { label: string; hours: number }[] = [
  { label: '24 hours', hours: 24 },
  { label: '7 days', hours: 168 },
  { label: '30 days', hours: 720 },
]

export function AuditLog({ tenant, user }: { tenant: string; user: string }) {
  const [entries, setEntries] = useState<AuditEntry[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [hours, setHours] = useState(168)
  const [q, setQ] = useState('')
  const [cat, setCat] = useState<string>('all')
  const [open, setOpen] = useState<string | null>(null)

  const load = useCallback(async () => {
    setError(null); setEntries(null)
    try {
      const to = new Date()
      const from = new Date(to.getTime() - hours * 3600_000)
      const list = await api.get<AuditEntry[]>(`/api/audit?from=${from.toISOString()}&to=${to.toISOString()}`)
      setEntries([...list].sort((a, b) => (a.occurredAt < b.occurredAt ? 1 : -1)))
    } catch (e) { setError(e instanceof ApiError ? e.message : String(e)); setEntries([]) }
  }, [hours])
  useEffect(() => { void load() }, [load, tenant, user])

  const categories = useMemo(() =>
    [...new Set((entries ?? []).map((e) => e.category))].sort(), [entries])

  const f = q.trim().toLowerCase()
  const rows = (entries ?? []).filter((e) =>
    (cat === 'all' || e.category === cat)
    && (!f || e.summary.toLowerCase().includes(f) || (e.actor ?? '').toLowerCase().includes(f) || e.category.toLowerCase().includes(f)))

  return (
    <div>
      <section className="panel">
        <h2>Audit trail</h2>
        <p className="muted">
          Every governed action this tenant recorded — control decisions, package and config changes,
          risk actions, billing — append-only and traceable. Filter by window, category, or text.
        </p>
        <div style={{ display: 'flex', gap: '0.6rem', alignItems: 'center', flexWrap: 'wrap' }}>
          <select value={hours} onChange={(e) => setHours(Number(e.target.value))}>
            {RANGES.map((r) => <option key={r.hours} value={r.hours}>Last {r.label}</option>)}
          </select>
          <select value={cat} onChange={(e) => setCat(e.target.value)}>
            <option value="all">All categories</option>
            {categories.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
          <input placeholder="Search summary, actor…" value={q} onChange={(e) => setQ(e.target.value)} style={{ minWidth: '14rem' }} />
          {entries && <span className="muted small">{rows.length} of {entries.length} entries</span>}
        </div>
        {error && <p className="error small">{error}</p>}
      </section>

      <section className="panel">
        {entries === null ? <SkeletonRows />
          : rows.length === 0 ? <p className="muted">No audit entries in this window.</p>
          : (
            <table className="pd-table">
              <thead><tr><th>When</th><th>Category</th><th>Actor</th><th>Summary</th></tr></thead>
              <tbody>
                {rows.slice(0, 500).map((e) => (
                  <Fragment key={e.id}>
                    <tr onClick={() => setOpen(open === e.id ? null : e.id)} style={{ cursor: e.detail && Object.keys(e.detail).length ? 'pointer' : 'default' }}>
                      <td className="muted small" style={{ whiteSpace: 'nowrap' }}>{new Date(e.occurredAt).toLocaleString()}</td>
                      <td><span className="cap-kind cap-event" style={{ minWidth: 0 }}>{e.category}</span></td>
                      <td className="muted small">{e.actor ?? '—'}</td>
                      <td>{e.summary}</td>
                    </tr>
                    {open === e.id && e.detail && Object.keys(e.detail).length > 0 && (
                      <tr><td colSpan={4} className="muted small" style={{ background: '#0d141c' }}>
                        {Object.entries(e.detail).map(([k, v]) => <span key={k} style={{ marginRight: '1rem' }}><span className="mono">{k}</span>={v || '∅'}</span>)}
                      </td></tr>
                    )}
                  </Fragment>
                ))}
              </tbody>
            </table>
          )}
      </section>
    </div>
  )
}
