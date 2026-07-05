import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api'

interface Alternative {
  targetReference: string
  score: number
  reason: string
}

interface Recommendation {
  id: string
  targetReference: string
  targetAssetClass: string | null
  amount: number
  direction: string
  targetInstitution: string | null
  targetJurisdiction: string | null
  confidenceScore: number
  reasoning: string
  assumptions: string[]
  riskFactors: string[]
  alternativesConsidered: Alternative[]
  tier: string
  status: string
  createdAt: string
  decidedBy: string | null
  decisionComment: string | null
  modifiedAmount: number | null
}

export function Allocations({ tenant, user }: { tenant: string; user: string }) {
  const [items, setItems] = useState<Recommendation[]>([])
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [modifyAmount, setModifyAmount] = useState('')

  const refresh = useCallback(async () => {
    try {
      setItems(await api.get<Recommendation[]>('/api/capital-flows?limit=20'))
      setError(null)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }, [])

  useEffect(() => {
    void refresh()
  }, [refresh, tenant, user])

  async function generate() {
    setError(null)
    setMessage(null)
    try {
      const result = await api.post<Recommendation | { message: string }>('/api/capital-flows/generate')
      if ('message' in result) setMessage(result.message)
      await refresh()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }

  async function decide(id: string, action: 'Approve' | 'Modify' | 'Reject') {
    setError(null)
    try {
      await api.post(`/api/capital-flows/${id}/decision`, {
        action,
        modifiedAmount: action === 'Modify' && modifyAmount ? Number(modifyAmount) : null,
        comment: null,
      })
      await refresh()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }

  const fmt = (n: number) => n.toLocaleString(undefined, { maximumFractionDigits: 2 })

  return (
    <div>
      <section className="panel">
        <h2>Capital Flows <span className="muted small">(outbound allocations &amp; sweeps; inbound draws &amp; capital calls)</span></h2>
        {error && <div className="banner banner-error">{error}</div>}
        {message && <div className="banner banner-ok">{message}</div>}
        <div className="form-row">
          <button onClick={generate}>Generate recommendation</button>
          <label>
            Modified amount (for Modify)
            <input value={modifyAmount} onChange={(e) => setModifyAmount(e.target.value)} placeholder="600000" />
          </label>
        </div>
      </section>

      {items.map((r) => (
        <section key={r.id} className="panel">
          <h2>
            {r.direction === 'Inbound' ? '⬅ ' : ''}{fmt(r.amount)} → <span className="accent">{r.targetReference}</span>{' '}
            <span className={`state state-${r.tier.toLowerCase()}`}>{r.tier}</span>{' '}
            <span className={`state state-${r.status.toLowerCase()}`}>{r.status}</span>
          </h2>
          <p className="small">
            Confidence <strong>{r.confidenceScore}</strong> · via {r.targetInstitution} ·{' '}
            {r.targetJurisdiction} · {new Date(r.createdAt).toLocaleString()}
          </p>
          <p>{r.reasoning}</p>

          <div className="two-col">
            <div>
              <h3>Assumptions</h3>
              {r.assumptions.map((a, i) => (
                <div key={i} className="card small">{a}</div>
              ))}
              <h3>Risk factors</h3>
              {r.riskFactors.map((rf, i) => (
                <div key={i} className="card small">{rf}</div>
              ))}
            </div>
            <div>
              <h3>Alternatives considered</h3>
              {r.alternativesConsidered.length === 0 && (
                <p className="muted small">No other candidates.</p>
              )}
              {r.alternativesConsidered.map((alt) => (
                <div key={alt.targetReference} className="card small">
                  <strong>{alt.targetReference}</strong> (score {alt.score}) — {alt.reason}
                </div>
              ))}
            </div>
          </div>

          {r.status === 'Pending' ? (
            <div className="approval-actions" style={{ marginTop: '0.6rem' }}>
              <button onClick={() => decide(r.id, 'Approve')}>Approve</button>
              <button onClick={() => decide(r.id, 'Modify')}>Modify</button>
              <button className="secondary" onClick={() => decide(r.id, 'Reject')}>Reject</button>
            </div>
          ) : (
            <p className="muted small">
              {r.decidedBy ? `Decided by ${r.decidedBy}` : 'Auto-executed within autonomous tier'}
              {r.modifiedAmount ? ` — amount modified to ${fmt(r.modifiedAmount)}` : ''}
              {r.decisionComment ? ` — ${r.decisionComment}` : ''}
            </p>
          )}
        </section>
      ))}
    </div>
  )
}
