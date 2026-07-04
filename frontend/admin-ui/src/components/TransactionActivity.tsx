import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api'
import type { ActivityItem } from '../types'

const POLL_MS = 3000

export function TransactionActivity({ tenant }: { tenant: string }) {
  const [items, setItems] = useState<ActivityItem[]>([])
  const [error, setError] = useState<string | null>(null)
  const [txType, setTxType] = useState('withdrawal')
  const [submittedBy, setSubmittedBy] = useState('treasurer@example.com')
  const [payloadJson, setPayloadJson] = useState('{"ventureId":"V-001","amount":60000}')

  const refresh = useCallback(async () => {
    try {
      setItems(await api.get<ActivityItem[]>('/api/transactions?limit=50'))
      setError(null)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }, [])

  useEffect(() => {
    void refresh()
    const timer = setInterval(() => void refresh(), POLL_MS)
    return () => clearInterval(timer)
  }, [refresh, tenant])

  async function submit() {
    setError(null)
    try {
      await api.post('/api/transactions', {
        transactionType: txType,
        submittedBy,
        payload: JSON.parse(payloadJson),
      })
      await refresh()
    } catch (e) {
      if (e instanceof SyntaxError) setError(`Payload is not valid JSON: ${e.message}`)
      else setError(e instanceof ApiError ? e.message : String(e))
    }
  }

  return (
    <div>
      <section className="panel">
        <h2>Submit test transaction</h2>
        {error && <div className="banner banner-error">{error}</div>}
        <div className="form-row">
          <label>
            Type
            <input value={txType} onChange={(e) => setTxType(e.target.value)} />
          </label>
          <label>
            Submitted by
            <input value={submittedBy} onChange={(e) => setSubmittedBy(e.target.value)} />
          </label>
          <label className="grow">
            Payload (JSON)
            <input value={payloadJson} onChange={(e) => setPayloadJson(e.target.value)} />
          </label>
          <button onClick={submit}>Submit</button>
        </div>
      </section>

      <section className="panel">
        <h2>Transaction Activity <span className="muted small">(auto-refreshes every 3s)</span></h2>
        <table>
          <thead>
            <tr>
              <th>Received</th>
              <th>Transaction</th>
              <th>Payload</th>
              <th>Outcome</th>
              <th>Fired rule / reason</th>
            </tr>
          </thead>
          <tbody>
            {items.length === 0 && (
              <tr>
                <td colSpan={5} className="muted">
                  No transactions yet for this tenant.
                </td>
              </tr>
            )}
            {items.map((t) => {
              const outcome = t.outcomes[t.outcomes.length - 1]
              return (
                <tr key={t.transactionId}>
                  <td className="small">{new Date(t.receivedAt).toLocaleTimeString()}</td>
                  <td>
                    <strong>{t.transactionType}</strong>
                    <div className="muted small">{t.submittedBy}</div>
                    <div className="muted small mono">{t.transactionId.slice(0, 8)}…</div>
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
                </tr>
              )
            })}
          </tbody>
        </table>
      </section>
    </div>
  )
}
