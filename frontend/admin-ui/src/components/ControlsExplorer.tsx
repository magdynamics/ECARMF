import { useCallback, useEffect, useMemo, useState } from 'react'
import { api } from '../api'
import type { PackageSummary } from '../types'

// Progressive Disclosure for controls (ECARMF-ADR-UIUX-001 Phase 1, §2.2).
// Replaces a flat control table with a Domain -> Category -> Control drill-down
// over the active packages' executable controls (their rules). A text filter
// and expand/collapse give role/persona-style scoping.

interface Rule {
  ruleId: string
  name: string
  description?: string | null
  outcomeOnMatch?: string | null
  triggerEvent?: string | null
}
interface FullPackage {
  manifest: { rules?: Rule[] }
}
interface Control extends Rule {
  packageId: string
  domain: string
  category: string
}

// Domain = the control id with the trailing "-NNN[a]" sequence removed
// (RCM-CMP-011 -> RCM-CMP, EF-012 -> EF, AI-GOV-001 -> AI-GOV).
function domainOf(ruleId: string): string {
  const d = ruleId.replace(/-\d+[a-z]?$/i, '')
  return d && d !== ruleId ? d : ruleId
}
function categoryOf(outcome?: string | null): string {
  switch ((outcome ?? '').toLowerCase()) {
    case 'rejected': return 'Preventive — blocks the action'
    case 'flagged': return 'Detective — flags for review'
    default: return 'Other'
  }
}

export function ControlsExplorer({ tenant, user }: { tenant: string; user: string }) {
  const [controls, setControls] = useState<Control[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [filter, setFilter] = useState('')
  const [openDomain, setOpenDomain] = useState<string | null>(null)
  const [openControl, setOpenControl] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    setError(null)
    try {
      const pkgs = await api.get<PackageSummary[]>('/api/packages')
      const active = pkgs.filter((p) => p.state === 'Active')
      const all: Control[] = []
      for (const p of active) {
        try {
          const full = await api.get<FullPackage>(`/api/packages/${p.packageId}/${p.packageVersion}`)
          for (const r of full.manifest.rules ?? []) {
            all.push({ ...r, packageId: p.packageId, domain: domainOf(r.ruleId), category: categoryOf(r.outcomeOnMatch) })
          }
        } catch { /* skip a package we can't read */ }
      }
      setControls(all)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load packages')
      setControls([])
    }
  }, [])

  useEffect(() => { void refresh() }, [refresh, tenant, user])

  // Domain -> Category -> Control[], with the text filter applied.
  const tree = useMemo(() => {
    const f = filter.trim().toLowerCase()
    const match = (c: Control) => !f
      || c.ruleId.toLowerCase().includes(f)
      || c.name.toLowerCase().includes(f)
      || c.domain.toLowerCase().includes(f)
      || (c.description ?? '').toLowerCase().includes(f)
    const byDomain = new Map<string, Map<string, Control[]>>()
    for (const c of (controls ?? []).filter(match)) {
      const cats = byDomain.get(c.domain) ?? byDomain.set(c.domain, new Map()).get(c.domain)!
      ;(cats.get(c.category) ?? cats.set(c.category, []).get(c.category)!).push(c)
    }
    return [...byDomain.entries()]
      .map(([d, cats]) => ({ domain: d, count: [...cats.values()].reduce((n, l) => n + l.length, 0), cats: [...cats.entries()].sort() }))
      .sort((a, b) => b.count - a.count)
  }, [controls, filter])

  const total = controls?.length ?? 0

  return (
    <div>
      <section className="panel">
        <h2>Controls</h2>
        <p className="muted">
          The executable controls active for tenant <strong>{tenant}</strong>, grouped
          <strong> domain → category → control</strong> instead of one flat table. Expand a domain
          to see its preventive and detective controls; open a control for what it does. Type to
          filter across every level.
        </p>
        <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center', flexWrap: 'wrap' }}>
          <input
            placeholder="Filter controls (id, name, domain, text)…"
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            style={{ minWidth: '18rem' }}
          />
          <span className="muted small">{tree.reduce((n, d) => n + d.count, 0)} of {total} control(s) · {tree.length} domain(s)</span>
        </div>
        {error && <p className="error small">{error}</p>}
      </section>

      {controls === null ? (
        <section className="panel"><p className="muted">Loading…</p></section>
      ) : total === 0 ? (
        <section className="panel"><h3>No active controls</h3><p className="muted">Activate a Knowledge Package with rules to populate this view.</p></section>
      ) : (
        <section className="panel">
          {tree.map((d) => {
            const open = openDomain === d.domain || !!filter
            return (
              <div key={d.domain} className="ctrl-domain">
                <button className="ctrl-domain-head" onClick={() => setOpenDomain(open && !filter ? null : d.domain)} aria-expanded={open}>
                  <span className="ctrl-caret">{open ? '▾' : '▸'}</span>
                  <strong className="mono">{d.domain}</strong>
                  <span className="muted small">{d.count} control(s)</span>
                </button>
                {open && d.cats.map(([cat, list]) => (
                  <div key={cat} className="ctrl-cat">
                    <div className="ctrl-cat-head">{cat} <span className="muted small">· {list.length}</span></div>
                    {list.map((c) => (
                      <div key={c.ruleId} className="ctrl-item">
                        <button className="ctrl-item-head" onClick={() => setOpenControl(openControl === c.ruleId ? null : c.ruleId)} aria-expanded={openControl === c.ruleId}>
                          <span className="mono small">{c.ruleId}</span> {c.name}
                        </button>
                        {openControl === c.ruleId && (
                          <div className="ctrl-item-body">
                            {c.description && <p className="small">{c.description}</p>}
                            <p className="muted small">
                              On <code>{c.triggerEvent ?? 'RecordReceived'}</code> → <strong>{c.outcomeOnMatch ?? '—'}</strong> · from <code>{c.packageId}</code>
                            </p>
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                ))}
              </div>
            )
          })}
        </section>
      )}
    </div>
  )
}
