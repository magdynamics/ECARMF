import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api'

interface AdvisorRecommendation {
  recommendation: string
  rationale: string
  priority: string
}

interface AdvisorBrief {
  id: string
  title: string
  executiveSummary: string
  recommendations: AdvisorRecommendation[]
  modelReference: string
  provenance: string
  requestedBy: string
  createdAt: string
  feedbackUseful: boolean | null
  feedbackBy: string | null
}

const PRIORITY_ORDER: Record<string, number> = { High: 0, Medium: 1, Low: 2 }

export function Advisor({ tenant, user }: { tenant: string; user: string }) {
  const [briefs, setBriefs] = useState<AdvisorBrief[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const load = useCallback(async () => {
    try {
      setBriefs(await api.get<AdvisorBrief[]>('/api/advisor/briefs'))
      setError('')
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load, tenant, user])

  async function generate() {
    setBusy(true)
    setError('')
    try {
      await api.post<AdvisorBrief>('/api/advisor/briefs')
      await load()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    } finally {
      setBusy(false)
    }
  }

  async function sendFeedback(id: string, useful: boolean) {
    setError('')
    try {
      await api.post<AdvisorBrief>(`/api/advisor/briefs/${id}/feedback`, { useful })
      await load()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }

  return (
    <section className="panel">
      <h2>Executive Advisor</h2>
      <p className="muted">
        An AI agent that reads this tenant's scores, deviation alerts, pending allocations, and open
        tasks, and writes an executive brief under its own identity (<code>system:advisor</code>).
        It advises only — it never decides. Rate each brief: your verdict feeds the advisor's
        ModelAccuracy trust score, the same loop every AI output goes through.
      </p>

      <button onClick={generate} disabled={busy}>
        {busy ? 'Analyzing…' : 'Generate Executive Brief'}
      </button>

      {error && <p className="error">{error}</p>}

      {briefs.length === 0 && !error && (
        <p className="muted">
          No briefs yet. Click "Generate Executive Brief" and the advisor will analyze this
          tenant's current state.
        </p>
      )}

      {briefs.map((brief) => (
        <div key={brief.id} className="card">
          <div className="card-header">
            <strong>{brief.title}</strong>
            <span className="muted">
              {new Date(brief.createdAt).toLocaleString()} · {brief.modelReference} ·{' '}
              {brief.provenance}
            </span>
          </div>
          <p>{brief.executiveSummary}</p>
          {[...brief.recommendations]
            .sort((a, b) => (PRIORITY_ORDER[a.priority] ?? 9) - (PRIORITY_ORDER[b.priority] ?? 9))
            .map((rec, i) => (
              <div key={i} className="recommendation">
                <span className={`state state-${rec.priority.toLowerCase()}`}>{rec.priority}</span>{' '}
                <strong>{rec.recommendation}</strong>
                <div className="muted">{rec.rationale}</div>
              </div>
            ))}
          <div className="card-actions">
            {brief.feedbackUseful === null ? (
              <>
                <span className="muted">Was this brief useful?</span>
                <button onClick={() => sendFeedback(brief.id, true)}>👍 Useful</button>
                <button onClick={() => sendFeedback(brief.id, false)}>👎 Not useful</button>
              </>
            ) : (
              <span className="muted">
                Rated {brief.feedbackUseful ? 'useful' : 'not useful'} by {brief.feedbackBy}
              </span>
            )}
          </div>
        </div>
      ))}
    </section>
  )
}
