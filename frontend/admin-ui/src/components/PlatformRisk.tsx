import { useEffect, useState } from 'react'
import { SkeletonRows } from './SkeletonRows'
import { api, ApiError } from '../api'
import { RiskHeatmap, type RiskPoint } from './RiskHeatmap'

// Platform-wide risk (operator). Every tenant has risks to manage; this rolls
// them all onto one heatmap and a per-tenant posture table, so risk can be
// watched across the whole client base — not one tenant at a time.

interface PlatformRiskPoint {
  tenantId: string; domain: string; severity: number; likelihood: number; index: number; label: string
}
interface TenantRiskSummary {
  tenantId: string; name: string; risks: number; critical: number; worstIndex: number; topDomains: string[]
}
interface PlatformRiskOverview {
  totalRisks: number; criticalRisks: number; tenantsWithRisk: number; tenantsTotal: number
  tenants: TenantRiskSummary[]; heatmap: PlatformRiskPoint[]
}

export function PlatformRisk({ onOpenTenant }: { onOpenTenant: (tenantId: string) => void }) {
  const [data, setData] = useState<PlatformRiskOverview | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [groupBy, setGroupBy] = useState<'tenant' | 'domain'>('tenant')

  useEffect(() => {
    api.get<PlatformRiskOverview>('/api/platform/risk')
      .then(setData)
      .catch((e) => setError(e instanceof ApiError ? e.message : String(e)))
  }, [])

  const points: RiskPoint[] = (data?.heatmap ?? []).map((p, i) => ({
    id: `${p.tenantId}:${p.label}:${i}`,
    label: p.label,
    severity: p.severity,
    likelihood: p.likelihood,
    group: groupBy === 'tenant' ? p.tenantId : p.domain,
    score: p.index,
  }))

  return (
    <div>
      <section className="panel">
        <h2>Platform risk</h2>
        <p className="muted">
          Every risk across every tenant, on one heatmap. Each tenant has risks to address; the table
          ranks them by how many sit in the critical zone. Click a tenant to open its own risk register.
        </p>
        {data && (
          <p className="small">
            <strong>{data.totalRisks.toLocaleString()}</strong> risks ·{' '}
            <strong className="error-text">{data.criticalRisks.toLocaleString()}</strong> critical ·{' '}
            {data.tenantsWithRisk} of {data.tenantsTotal} tenants have risk data
          </p>
        )}
        {error && <p className="error small">{error}</p>}
      </section>

      {data && data.totalRisks > 0 && (
        <section className="panel">
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: '0.5rem' }}>
            <h3>Combined heatmap</h3>
            <label>Band by
              <select value={groupBy} onChange={(e) => setGroupBy(e.target.value as 'tenant' | 'domain')}>
                <option value="tenant">Tenant</option>
                <option value="domain">Risk domain</option>
              </select>
            </label>
          </div>
          <RiskHeatmap risks={points} />
        </section>
      )}

      <section className="panel">
        <h3>By tenant</h3>
        {data === null ? <SkeletonRows />
          : data.tenants.length === 0 ? <p className="muted">No risk-tagged data on any tenant yet. Add a risk-register skill to start tracking.</p>
          : (
            <table className="pd-table">
              <thead><tr><th>Tenant</th><th>Risks</th><th>Critical</th><th>Top domains</th><th>Worst index</th><th></th></tr></thead>
              <tbody>
                {data.tenants.map((t) => (
                  <tr key={t.tenantId}>
                    <td><strong>{t.name}</strong><div className="muted small">{t.tenantId}</div></td>
                    <td>{t.risks}</td>
                    <td className={t.critical > 0 ? 'error-text' : 'muted'}>{t.critical}</td>
                    <td className="muted small">{t.topDomains.join(', ')}</td>
                    <td>{t.worstIndex}</td>
                    <td><button className="secondary small" onClick={() => onOpenTenant(t.tenantId)}>Open →</button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
      </section>
    </div>
  )
}
