import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api'
import type { ActivityItem } from '../types'

const POLL_MS = 3000

export function RecordActivity({ tenant, user }: { tenant: string; user: string }) {
  const [items, setItems] = useState<ActivityItem[]>([])
  const [error, setError] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    try {
      setItems(await api.get<ActivityItem[]>('/api/records?limit=50'))
      setError(null)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }, [])

  useEffect(() => {
    void refresh()
    const timer = setInterval(() => void refresh(), POLL_MS)
    return () => clearInterval(timer)
  }, [refresh, tenant, user])

  async function decide(recordId: string, verdict: 'Approve' | 'Reject') {
    setError(null)
    try {
      // The approver is the authenticated identity; segregation of duties
      // and permissions are enforced server-side.
      await api.post(`/api/records/${recordId}/approvals`, {
        verdict,
        comment: null,
      })
      await refresh()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }

  return (
    <div>
      {error && <div className="banner banner-error">{error}</div>}
      <section className="panel">
        <h2>
          Record Activity <span className="state state-approved">OUTPUT</span>{' '}
          <span className="muted small">(auto-refreshes every 3s)</span>
        </h2>
        <p className="muted small">
          Every record that entered the pipeline, what the kernel decided, which rule decided it,
          and why. Flagged items show Approve/Reject — decided as the identity in the header
          (approver must differ from the submitter). To add data, use the Data Entry screen.
        </p>
        <table>
          <thead>
            <tr>
              <th>Received</th>
              <th>Record</th>
              <th>Payload</th>
              <th>Outcome</th>
              <th>Fired rule / reason</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {items.length === 0 && (
              <tr>
                <td colSpan={6} className="muted">
                  No records yet for this tenant.
                </td>
              </tr>
            )}
            {items.map((t) => {
              const outcome = t.outcomes[t.outcomes.length - 1]
              const isFlagged = outcome?.outcome?.toLowerCase() === 'flagged'
              return (
                <tr key={t.recordId}>
                  <td className="small">{new Date(t.receivedAt).toLocaleTimeString()}</td>
                  <td>
                    <strong>{t.recordType}</strong>
                    <div className="muted small">{t.submittedBy}</div>
                    <div className="muted small mono">{t.recordId.slice(0, 8)}…</div>
                  </td>
                  <td className="small mono">
                    {Object.entries(t.payload)
                      .map(([k, v]) => `${k}=${v}`)
                      .join(' ')}
                  </td>
                  <td>
                    {outcome ? (
                      <span className={`state state-${outcome.outcome.toLowerCase()}`}>
                        {outcome.outcome}
                      </span>
                    ) : (
                      <span className="state state-pending">Pending</span>
                    )}
                  </td>
                  <td className="small">
                    {outcome ? (
                      <>
                        {outcome.ruleId ? (
                          <div>
                            <code>{outcome.ruleId}</code>{' '}
                            <span className="muted">
                              ({outcome.packageId} v{outcome.packageVersion})
                            </span>
                          </div>
                        ) : (
                          <div className="muted">default kernel policy</div>
                        )}
                        <div className="muted">{outcome.reason}</div>
                      </>
                    ) : (
                      <span className="muted">awaiting processing…</span>
                    )}
                  </td>
                  <td>
                    {isFlagged && (
                      <div className="approval-actions">
                        <button onClick={() => decide(t.recordId, 'Approve')}>Approve</button>
                        <button className="secondary" onClick={() => decide(t.recordId, 'Reject')}>
                          Reject
                        </button>
                      </div>
                    )}
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </section>
    </div>
  )
}
