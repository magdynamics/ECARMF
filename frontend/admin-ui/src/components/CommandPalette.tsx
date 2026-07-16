import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { api } from '../api'

// Global search (⌘K / Ctrl-K). One box that searches everything and adapts to
// where you are: on the operator tenant it finds tenants and skills; inside a
// tenant it finds that tenant's controls, KPIs, agents, entities, knowledge,
// records, and renewals. Reuses existing endpoints; results navigate on Enter.

interface Hit { kind: string; title: string; subtitle?: string; onGo: () => void }

const SCREENS: { label: string; tab: string; kw: string }[] = [
  { label: 'Start Here', tab: 'home', kw: 'home start' },
  { label: 'System Map', tab: 'systemmap', kw: 'guide journey' },
  { label: 'Dictionary', tab: 'glossary', kw: 'help glossary terms definitions' },
  { label: 'Capability Explorer', tab: 'explore', kw: 'capabilities search index' },
  { label: 'Organization', tab: 'organization', kw: 'org units setup' },
  { label: 'Packages', tab: 'packages', kw: 'packages manifests' },
  { label: 'Controls', tab: 'controls', kw: 'controls rules governance' },
  { label: 'Data Entry', tab: 'dataentry', kw: 'submit record input' },
  { label: 'Record Activity', tab: 'activity', kw: 'records activity outcomes' },
  { label: 'Dashboard', tab: 'dashboard', kw: 'dashboard kpis metrics' },
  { label: 'Risk Register', tab: 'risk', kw: 'risk heatmap' },
  { label: 'Reports', tab: 'reports', kw: 'reports' },
  { label: 'Library', tab: 'library', kw: 'documents library' },
  { label: 'AI Advisor', tab: 'advisor', kw: 'ai advisor' },
  { label: 'AI Agents', tab: 'agents', kw: 'ai agents consult' },
  { label: 'Renewals', tab: 'renewals', kw: 'renewals due' },
  { label: 'Benchmarks', tab: 'benchmarks', kw: 'benchmarks thresholds' },
  { label: 'Skills Library', tab: 'skillslibrary', kw: 'skills value controls assertions' },
  { label: 'Package Library', tab: 'catalog', kw: 'catalog install' },
  { label: 'Demo Twins', tab: 'demos', kw: 'demo training' },
  { label: 'Skills', tab: 'skills', kw: 'skills activate billing' },
]

interface CapabilityItem { kind: string; id: string; name: string; packageId: string }

const capTab = (kind: string) =>
  kind === 'Control' ? 'controls' : kind === 'KPI' ? 'dashboard' : kind === 'Agent' ? 'agents' : 'explore'

export function CommandPalette({ tenant, isPlatform, go, onOpenTenant, onClose }: {
  tenant: string
  isPlatform: boolean
  go: (tab: string) => void
  onOpenTenant: (id: string) => void
  onClose: () => void
}) {
  const [q, setQ] = useState('')
  const [caps, setCaps] = useState<{ kind: string; id: string; name: string }[]>([])
  const [tenants, setTenants] = useState<{ tenantId: string; name: string }[]>([])
  const [skills, setSkills] = useState<{ packageId: string; name: string }[]>([])
  const [renewals, setRenewals] = useState<{ name: string }[]>([])
  const [records, setRecords] = useState<{ recordId: string; recordType: string }[]>([])
  const [sel, setSel] = useState(0)
  const inputRef = useRef<HTMLInputElement>(null)

  // Load the searchable context for the current place, once.
  useEffect(() => {
    inputRef.current?.focus()
    let live = true
    ;(async () => {
      if (isPlatform) {
        try { const t = await api.get<{ tenantId: string; name: string }[]>('/api/platform/tenants'); if (live) setTenants(t) } catch { /* not operator */ }
        try { const s = await api.get<{ packageId: string; name: string }[]>('/api/catalog'); if (live) setSkills(s) } catch { /* */ }
      } else {
        try {
          // One server-side index call — previously a per-package manifest fan-out.
          const items = await api.get<CapabilityItem[]>('/api/capabilities')
          if (live) setCaps(items.map((i) => ({ kind: i.kind, id: i.id, name: i.name })))
        } catch { /* */ }
        try { const r = await api.get<{ name: string }[]>('/api/renewals'); if (live) setRenewals(r) } catch { /* */ }
      }
    })()
    return () => { live = false }
  }, [tenant, isPlatform])

  // Records need a server query (they aren't preloaded).
  useEffect(() => {
    if (isPlatform) return
    const term = q.trim()
    if (term.length < 2) { setRecords([]); return }
    const h = setTimeout(async () => {
      try {
        const r = await api.get<{ recordId: string; recordType: string }[]>(`/api/records?search=${encodeURIComponent(term)}&pageSize=6`)
        setRecords(r)
      } catch { setRecords([]) }
    }, 220)
    return () => clearTimeout(h)
  }, [q, isPlatform])

  const f = q.trim().toLowerCase()
  const match = useCallback((...s: (string | undefined)[]) => !f || s.some((x) => (x ?? '').toLowerCase().includes(f)), [f])

  const hits = useMemo<Hit[]>(() => {
    const out: Hit[] = []
    for (const s of SCREENS) if (match(s.label, s.kw)) out.push({ kind: 'Screen', title: s.label, onGo: () => go(s.tab) })
    if (isPlatform) {
      for (const t of tenants) if (match(t.tenantId, t.name)) out.push({ kind: 'Tenant', title: t.name, subtitle: t.tenantId, onGo: () => onOpenTenant(t.tenantId) })
      for (const s of skills) if (match(s.packageId, s.name)) out.push({ kind: 'Skill', title: s.name, subtitle: s.packageId, onGo: () => go('skillslibrary') })
    } else {
      for (const c of caps) if (match(c.id, c.name)) out.push({ kind: c.kind, title: c.name, subtitle: c.id, onGo: () => go(capTab(c.kind)) })
      for (const r of records) out.push({ kind: 'Record', title: r.recordType, subtitle: r.recordId.slice(0, 8), onGo: () => go('activity') })
      for (const r of renewals) if (match(r.name)) out.push({ kind: 'Renewal', title: r.name, onGo: () => go('renewals') })
    }
    // Screens first, then the rest; cap for performance.
    return out.slice(0, 60)
  }, [match, isPlatform, tenants, skills, caps, records, renewals, go, onOpenTenant])

  useEffect(() => { setSel(0) }, [q])

  function onKey(e: React.KeyboardEvent) {
    if (e.key === 'Escape') onClose()
    else if (e.key === 'ArrowDown') { e.preventDefault(); setSel((s) => Math.min(s + 1, hits.length - 1)) }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setSel((s) => Math.max(s - 1, 0)) }
    else if (e.key === 'Enter' && hits[sel]) { hits[sel].onGo(); onClose() }
  }

  return (
    <div className="cmdk-backdrop" onClick={onClose}>
      <div className="cmdk" onClick={(e) => e.stopPropagation()}>
        <input
          ref={inputRef}
          className="cmdk-input"
          placeholder={isPlatform ? 'Search tenants, skills, screens…' : 'Search controls, KPIs, agents, records, renewals…'}
          value={q}
          onChange={(e) => setQ(e.target.value)}
          onKeyDown={onKey}
        />
        <div className="cmdk-results">
          {hits.length === 0 ? (
            <div className="cmdk-empty">{q ? 'No matches.' : 'Type to search everything.'}</div>
          ) : hits.map((h, i) => (
            <button
              key={`${h.kind}:${h.title}:${i}`}
              className={`cmdk-row ${i === sel ? 'sel' : ''}`}
              onMouseEnter={() => setSel(i)}
              onClick={() => { h.onGo(); onClose() }}
            >
              <span className={`cap-kind cap-${h.kind.toLowerCase()}`}>{h.kind}</span>
              <span className="cmdk-title">{h.title}</span>
              {h.subtitle && <span className="mono small cmdk-sub">{h.subtitle}</span>}
            </button>
          ))}
        </div>
        <div className="cmdk-foot muted small">↑↓ navigate · ↵ open · esc close</div>
      </div>
    </div>
  )
}
