import { useCallback, useEffect, useState } from 'react'
import { Icon } from './Icon'
import { api, ApiError } from '../api'

interface AdvisorRecommendation {
  recommendation: string
  rationale: string
  priority: string
}

interface DeclaredAgent {
  agentId: string
  name: string
  description: string | null
  sampleQuestions: string[]
  packageId: string
  packageVersion: string
}

interface AgentInteraction {
  id: string
  agentId: string
  question: string
  answer: string
  modelReference: string
  askedBy: string
  askedAt: string
  feedbackUseful: boolean | null
  feedbackBy: string | null
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
  const [agents, setAgents] = useState<DeclaredAgent[]>([])
  const [selectedAgent, setSelectedAgent] = useState('')
  const [question, setQuestion] = useState('')
  const [asking, setAsking] = useState(false)
  const [interactions, setInteractions] = useState<AgentInteraction[]>([])

  const load = useCallback(async () => {
    try {
      setBriefs(await api.get<AdvisorBrief[]>('/api/advisor/briefs'))
      const declared = await api.get<DeclaredAgent[]>('/api/agents')
      setAgents(declared)
      if (declared.length > 0) setSelectedAgent((s) => s || declared[0].agentId)
      setInteractions(await api.get<AgentInteraction[]>('/api/agents/interactions?limit=10'))
      setError('')
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }, [])

  async function ask() {
    setAsking(true)
    setError('')
    try {
      await api.post(`/api/agents/${selectedAgent}/ask`, { question })
      setQuestion('')
      setInteractions(await api.get<AgentInteraction[]>('/api/agents/interactions?limit=10'))
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    } finally {
      setAsking(false)
    }
  }

  async function rateAnswer(id: string, useful: boolean) {
    setError('')
    try {
      await api.post(`/api/agents/interactions/${id}/feedback`, { useful })
      setInteractions(await api.get<AgentInteraction[]>('/api/agents/interactions?limit=10'))
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }

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

  const currentAgent = agents.find((a) => a.agentId === selectedAgent)

  return (
    <div>
    <section className="panel">
      <h2>Specialized AI Agents <span className="state state-approved">OUTPUT</span></h2>
      <p className="muted small">
        Knowledge Packages ship domain-specialist agents the same way they ship rules — an IRS
        guide, a compliance guide, an operations analyst. Each agent sees only its declared tenant
        context, is advisory-only by kernel guardrail, acts under its own identity, and earns trust
        from your ratings. Consulting an agent uses this tenant's own AI credential (Setup → AI
        Backend).
      </p>
      {error && <p className="error">{error}</p>}
      {agents.length === 0 ? (
        <p className="muted">No active package declares an agent yet — activate a package that ships one (e.g. IRS Corporate Tax Rates 1.2.0).</p>
      ) : (
        <>
          <div className="form-row">
            <label>Agent<select value={selectedAgent} onChange={(e) => setSelectedAgent(e.target.value)}>
              {agents.map((a) => (
                <option key={a.agentId} value={a.agentId}>{a.name} ({a.packageId} v{a.packageVersion})</option>
              ))}
            </select></label>
            <button onClick={ask} disabled={asking || !question.trim()}>
              {asking ? 'Consulting…' : 'Ask'}
            </button>
          </div>
          {asking && (
            <p className="muted small" style={{ marginTop: '0.4rem' }}>
              <span className="spinner-dot" /> Consulting {currentAgent?.name ?? 'the agent'}… on a
              local model (Ollama) this usually takes <strong>20–30 seconds</strong>. The answer will
              appear just below when it's ready — no need to refresh.
            </p>
          )}
          <textarea
            rows={2}
            placeholder={currentAgent?.sampleQuestions[0] ?? 'Ask a question grounded in this tenant’s data…'}
            value={question}
            onChange={(e) => setQuestion(e.target.value)}
          />
          {currentAgent && currentAgent.sampleQuestions.length > 0 && (
            <p className="muted small">
              Try: {currentAgent.sampleQuestions.map((q, i) => (
                <button key={i} className="secondary" style={{ margin: '0 0.3rem 0.3rem 0' }}
                  onClick={() => setQuestion(q)}>{q}</button>
              ))}
            </p>
          )}
          {interactions.map((i) => (
            <div key={i.id} className="card">
              <div className="card-header">
                <strong>{i.question}</strong>
                <span className="muted small">
                  {agents.find((a) => a.agentId === i.agentId)?.name ?? i.agentId} ·{' '}
                  {new Date(i.askedAt).toLocaleString()} · {i.askedBy}
                </span>
              </div>
              <p style={{ whiteSpace: 'pre-wrap' }}>{i.answer}</p>
              <div className="card-actions">
                {i.feedbackUseful === null ? (
                  <>
                    <span className="muted">Was this answer useful?</span>
                    <button className="secondary" onClick={() => rateAnswer(i.id, true)} aria-label="Useful"><Icon name="thumbs-up" size={14} /></button>
                    <button className="secondary" onClick={() => rateAnswer(i.id, false)} aria-label="Not useful"><Icon name="thumbs-down" size={14} /></button>
                  </>
                ) : (
                  <span className="muted">Rated {i.feedbackUseful ? 'useful' : 'not useful'} by {i.feedbackBy}</span>
                )}
              </div>
            </div>
          ))}
        </>
      )}
    </section>

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
                <button className="secondary" onClick={() => sendFeedback(brief.id, true)}><Icon name="thumbs-up" size={14} /> Useful</button>
                <button className="secondary" onClick={() => sendFeedback(brief.id, false)}><Icon name="thumbs-down" size={14} /> Not useful</button>
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
    </div>
  )
}
