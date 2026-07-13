import { useEffect, useState } from 'react'
import { api } from '../api'

interface TenantCard {
  tenantId: string
  name: string
  industry: string | null
  status: string
  sensitivityTier?: string | null
}

/**
 * The operator's landing page: a clickable grid of every client tenant, so
 * switching into a workspace is one click — no typing a tenant id. Shown when
 * a platform operator is on the reserved 'platform' tenant (which has no
 * business data of its own).
 */
export function OperatorHome({ onPick }: { onPick: (tenantId: string) => void }) {
  const [tenants, setTenants] = useState<TenantCard[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    api
      .get<TenantCard[]>('/api/platform/tenants')
      .then((list) => setTenants(list))
      .catch(() => setError('Could not load the client list.'))
      .finally(() => setLoading(false))
  }, [])

  return (
    <section className="panel">
      <h2>Your clients</h2>
      <p className="muted">
        You are the platform operator. Pick a client to open its workspace — packages, records,
        dashboards, renewals, and agents all scoped to that tenant. This <code>platform</code> view
        is the operator console (Clients, Billing, Health) and holds no business data of its own.
      </p>

      {loading && <p className="muted">Loading clients…</p>}
      {error && <p className="error-text">{error}</p>}

      <div className="client-grid">
        {tenants.map((t) => (
          <button key={t.tenantId} className="client-card" onClick={() => onPick(t.tenantId)}>
            <div className="client-card-head">
              <strong>{t.name || t.tenantId}</strong>
              <span className={`state state-${t.status === 'Active' ? 'approved' : 'flagged'}`}>{t.status}</span>
            </div>
            <div className="client-card-meta">
              <span className="mono small">{t.tenantId}</span>
              {t.industry && <span className="muted small">· {t.industry}</span>}
              {t.sensitivityTier && t.sensitivityTier !== 'Standard' && (
                <span className="tier-badge">{t.sensitivityTier}</span>
              )}
            </div>
            <span className="client-card-cta">Open workspace →</span>
          </button>
        ))}
      </div>
    </section>
  )
}
