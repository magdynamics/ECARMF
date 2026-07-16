import { useCallback, useEffect, useMemo, useState } from 'react'
import { api } from '../api'

// Unified Capability & Knowledge Explorer. Everything a tenant can DO and
// KNOWS is spread across registries (rules, KPIs, agents, entities, events,
// knowledge assets) and buried in package manifests. This is one searchable
// index across all of it — "what can this tenant do?" in a single place.
// Served by GET /api/capabilities (one request; previously this fetched
// every active package's manifest individually).

type Kind = 'Control' | 'KPI' | 'Agent' | 'Entity' | 'Event' | 'Knowledge'
const KINDS: Kind[] = ['Control', 'KPI', 'Agent', 'Entity', 'Event', 'Knowledge']

interface Cap {
  kind: Kind
  id: string
  name: string
  desc?: string
  pkg: string
}

interface CapabilityItem {
  kind: string
  id: string
  name: string
  description?: string | null
  packageId: string
}

export function CapabilityExplorer({ tenant, user }: { tenant: string; user: string }) {
  const [caps, setCaps] = useState<Cap[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [filter, setFilter] = useState('')
  const [kinds, setKinds] = useState<Set<Kind>>(new Set(KINDS))

  const refresh = useCallback(async () => {
    setError(null)
    try {
      const items = await api.get<CapabilityItem[]>('/api/capabilities')
      setCaps(items.map((i) => ({
        kind: i.kind as Kind, id: i.id, name: i.name, desc: i.description ?? '', pkg: i.packageId,
      })))
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load capabilities')
      setCaps([])
    }
  }, [])

  useEffect(() => { void refresh() }, [refresh, tenant, user])

  const counts = useMemo(() => {
    const c = Object.fromEntries(KINDS.map((k) => [k, 0])) as Record<Kind, number>
    for (const cap of caps ?? []) c[cap.kind]++
    return c
  }, [caps])

  const results = useMemo(() => {
    const f = filter.trim().toLowerCase()
    return (caps ?? []).filter((c) => kinds.has(c.kind) && (!f
      || c.id.toLowerCase().includes(f) || c.name.toLowerCase().includes(f)
      || (c.desc ?? '').toLowerCase().includes(f) || c.pkg.toLowerCase().includes(f)))
  }, [caps, filter, kinds])

  function toggleKind(k: Kind) {
    const next = new Set(kinds)
    next.has(k) ? next.delete(k) : next.add(k)
    setKinds(next.size === 0 ? new Set(KINDS) : next)
  }

  const total = caps?.length ?? 0

  return (
    <div>
      <section className="panel">
        <h2>Capability Explorer</h2>
        <p className="muted">
          Everything tenant <strong>{tenant}</strong> can do and knows — its controls, KPIs, agents,
          entities, events, and knowledge assets — in one searchable index across every active package.
        </p>
        <input
          placeholder="Search everything (id, name, description, package)…"
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          style={{ width: '100%', maxWidth: '32rem' }}
        />
        <div className="cap-chips">
          {KINDS.map((k) => (
            <button key={k} className={`cap-chip ${kinds.has(k) ? 'on' : ''}`} onClick={() => toggleKind(k)}>
              {k} <span className="cap-chip-n">{counts[k]}</span>
            </button>
          ))}
          <span className="muted small">{results.length} of {total} shown</span>
        </div>
        {error && <p className="error small">{error}</p>}
      </section>

      <section className="panel">
        {caps === null ? (
          <p className="muted">Loading capabilities across active packages…</p>
        ) : results.length === 0 ? (
          <p className="muted">Nothing matches. {total === 0 ? 'Activate a package to populate this view.' : 'Try a broader search or enable more kinds.'}</p>
        ) : (
          <div className="cap-list">
            {results.slice(0, 400).map((c) => (
              <div key={`${c.kind}:${c.id}:${c.pkg}`} className="cap-row">
                <span className={`cap-kind cap-${c.kind.toLowerCase()}`}>{c.kind}</span>
                <div className="cap-main">
                  <div><span className="mono small">{c.id}</span> {c.name !== c.id && <strong>{c.name}</strong>}</div>
                  {c.desc && <div className="muted small">{c.desc}</div>}
                </div>
                <span className="mono small cap-pkg">{c.pkg.replace('ecarmf.', '')}</span>
              </div>
            ))}
            {results.length > 400 && <p className="muted small">Showing first 400 of {results.length} — narrow the search.</p>}
          </div>
        )}
      </section>
    </div>
  )
}
