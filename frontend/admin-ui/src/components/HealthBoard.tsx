import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api'

interface TenantHealth {
  tenantId: string
  name: string
  status: string
  criticalAlertsOpen: number
  warningAlertsOpen: number
  openTasks: number
  renewalsOverdue: number
  renewalsDueSoon: number
  feedsFailing: number
  recordsThisMonth: number
  documentsThisMonth: number
  lastFeedRun: string | null
}

function attentionBadge(t: TenantHealth) {
  const loud = t.criticalAlertsOpen + t.renewalsOverdue + t.feedsFailing
  if (loud > 0) return <span className="state state-failed">NEEDS ATTENTION</span>
  if (t.warningAlertsOpen + t.renewalsDueSoon > 0) return <span className="state state-flagged">WATCH</span>
  return <span className="state state-approved">HEALTHY</span>
}

/// The portfolio board: every client's posture on one screen, worst first —
/// replaces checking tenants one by one.
export function HealthBoard({ tenant, user }: { tenant: string; user: string }) {
  const [board, setBoard] = useState<TenantHealth[]>([])
  const [error, setError] = useState<string | null>(null)
  const [loadedAt, setLoadedAt] = useState<Date | null>(null)

  const load = useCallback(async () => {
    try {
      setBoard(await api.get<TenantHealth[]>('/api/platform/health'))
      setLoadedAt(new Date())
      setError(null)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load, tenant, user])

  return (
    <div>
      {error && <div className="banner banner-error">{error}</div>}

      <section className="panel">
        <h2>Client health board <span className="state state-staged">PLATFORM</span></h2>
        <p className="muted small">
          Every client on one screen, worst first: open critical alarms, overdue renewals, and
          failing feeds put a client at the top. {loadedAt && <>Refreshed {loadedAt.toLocaleTimeString()}. </>}
          <button className="secondary" onClick={load}>Refresh</button>
        </p>

        <table>
          <thead>
            <tr>
              <th>Client</th><th></th><th>Alerts (crit/warn)</th><th>Open tasks</th>
              <th>Renewals (overdue/soon)</th><th>Feeds failing</th><th>Records MTD</th><th>Docs MTD</th>
            </tr>
          </thead>
          <tbody>
            {board.map((t) => (
              <tr key={t.tenantId}>
                <td>
                  <strong>{t.name}</strong> <span className="muted mono small">{t.tenantId}</span>
                  {t.status !== 'Active' && <span className="state state-deactivated"> {t.status}</span>}
                </td>
                <td>{attentionBadge(t)}</td>
                <td>
                  <span className={t.criticalAlertsOpen > 0 ? 'state state-failed' : 'muted'}>{t.criticalAlertsOpen}</span>
                  {' / '}
                  <span className={t.warningAlertsOpen > 0 ? 'state state-flagged' : 'muted'}>{t.warningAlertsOpen}</span>
                </td>
                <td>{t.openTasks}</td>
                <td>
                  <span className={t.renewalsOverdue > 0 ? 'state state-failed' : 'muted'}>{t.renewalsOverdue}</span>
                  {' / '}
                  <span className={t.renewalsDueSoon > 0 ? 'state state-flagged' : 'muted'}>{t.renewalsDueSoon}</span>
                </td>
                <td><span className={t.feedsFailing > 0 ? 'state state-failed' : 'muted'}>{t.feedsFailing}</span></td>
                <td>{t.recordsThisMonth}</td>
                <td>{t.documentsThisMonth}</td>
              </tr>
            ))}
            {board.length === 0 && !error && (
              <tr><td colSpan={8} className="muted">No client tenants onboarded yet.</td></tr>
            )}
          </tbody>
        </table>
      </section>
    </div>
  )
}
