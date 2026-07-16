import { useEffect, useState } from 'react'
import { SkeletonRows } from './SkeletonRows'
import { Icon } from './Icon'
import { api, ApiError } from '../api'

// Operator action center — the whole client base as one ranked to-do:
// critical risks and renewals coming due, most urgent first, each linking
// straight into the tenant screen that resolves it.

interface ActionItem {
  tenantId: string; tenantName: string; type: string; title: string; detail: string; urgency: number; tab: string
}
interface PlatformActions { total: number; urgent: number; items: ActionItem[] }

function band(u: number) {
  return u >= 90 ? { cls: 'act-urgent', label: 'Now' } : u >= 70 ? { cls: 'act-high', label: 'Soon' } : { cls: 'act-med', label: 'Watch' }
}

export function PlatformActions({ onOpenTenant }: { onOpenTenant: (tenantId: string, tab: string) => void }) {
  const [data, setData] = useState<PlatformActions | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [filter, setFilter] = useState<'all' | 'Critical risk' | 'Renewal'>('all')

  useEffect(() => {
    api.get<PlatformActions>('/api/platform/actions')
      .then(setData)
      .catch((e) => setError(e instanceof ApiError ? e.message : String(e)))
  }, [])

  const items = (data?.items ?? []).filter((i) => filter === 'all' || i.type === filter)

  return (
    <div>
      <section className="panel">
        <h2>Action center</h2>
        <p className="muted">
          Everything across the client base that needs attention — critical risks and renewals coming
          due — ranked by urgency. Click an item to open the tenant where you resolve it.
        </p>
        <div style={{ display: 'flex', gap: '0.6rem', alignItems: 'center', flexWrap: 'wrap' }}>
          <select value={filter} onChange={(e) => setFilter(e.target.value as typeof filter)}>
            <option value="all">All types</option>
            <option value="Critical risk">Critical risks</option>
            <option value="Renewal">Renewals</option>
          </select>
          {data && <span className="muted small"><strong className="error-text">{data.urgent}</strong> need action now · {data.total} total</span>}
        </div>
        {error && <p className="error small">{error}</p>}
      </section>

      <section className="panel">
        {data === null ? <SkeletonRows />
          : items.length === 0 ? <p className="muted"><Icon name="check" size={14} /> Nothing needs attention.</p>
          : (
            <div className="act-list">
              {items.map((i, idx) => {
                const b = band(i.urgency)
                return (
                  <button key={idx} className="act-row" onClick={() => onOpenTenant(i.tenantId, i.tab)}>
                    <span className={`act-badge ${b.cls}`}>{b.label}</span>
                    <div className="act-main">
                      <div><span className="act-type">{i.type}</span> <strong>{i.title}</strong></div>
                      <div className="muted small">{i.tenantName} · {i.detail}</div>
                    </div>
                    <span className="act-go">Open →</span>
                  </button>
                )
              })}
            </div>
          )}
      </section>
    </div>
  )
}
