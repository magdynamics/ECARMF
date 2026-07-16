import { useCallback, useEffect, useState } from 'react'
import { SkeletonRows } from './SkeletonRows'
import { api } from '../api'

// Agents gallery + consult. 54 agents are declared across the packages but
// there was nowhere to actually USE them, even though AgentConsultService
// already runs them. This surfaces every agent for the tenant, shows its
// identity/guardrails, and lets an operator ask it a grounded question.

interface Agent {
  agentId: string
  name: string
  description?: string | null
  contextSources?: string[]
  sampleQuestions?: string[]
  outputDisclaimer?: string | null
  owner?: string | null
  independentValidator?: string | null
  riskTier?: string | null
  prohibited?: string[]
  packageId: string
}

interface Interaction {
  id: string
  question: string
  answer: string
  modelReference?: string | null
  provenance?: string | null
}

export function Agents({ tenant, user }: { tenant: string; user: string }) {
  const [agents, setAgents] = useState<Agent[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [filter, setFilter] = useState('')
  const [selected, setSelected] = useState<Agent | null>(null)
  const [question, setQuestion] = useState('')
  const [answer, setAnswer] = useState<Interaction | null>(null)
  const [asking, setAsking] = useState(false)
  const [askError, setAskError] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    setError(null)
    try {
      setAgents(await api.get<Agent[]>('/api/agents'))
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load agents')
      setAgents([])
    }
  }, [])

  useEffect(() => { void refresh() }, [refresh, tenant, user])

  function pick(a: Agent) {
    setSelected(a)
    setQuestion('')
    setAnswer(null)
    setAskError(null)
  }

  async function ask(q?: string) {
    if (!selected) return
    const text = (q ?? question).trim()
    if (!text) return
    setAsking(true)
    setAskError(null)
    setAnswer(null)
    try {
      setAnswer(await api.post<Interaction>(`/api/agents/${selected.agentId}/ask`, { question: text }))
    } catch (e) {
      setAskError(e instanceof Error ? e.message : 'The agent could not answer')
    } finally {
      setAsking(false)
    }
  }

  const f = filter.trim().toLowerCase()
  const list = (agents ?? []).filter((a) =>
    !f || a.agentId.toLowerCase().includes(f) || a.name.toLowerCase().includes(f) || (a.description ?? '').toLowerCase().includes(f))

  return (
    <div>
      <section className="panel">
        <h2>AI Agents</h2>
        <p className="muted">
          The specialized advisory agents available to tenant <strong>{tenant}</strong>. Each is
          grounded only in its declared context and is advisory-only. Pick one and ask — answers use
          the tenant's own AI credential when configured, or a deterministic composer otherwise.
        </p>
        <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center', flexWrap: 'wrap' }}>
          <input placeholder="Filter agents…" value={filter} onChange={(e) => setFilter(e.target.value)} style={{ minWidth: '16rem' }} />
          <span className="muted small">{list.length} agent(s)</span>
        </div>
        {error && <p className="error small">{error}</p>}
      </section>

      <div className="agents-layout">
        <section className="panel agents-list">
          {agents === null ? (
            <SkeletonRows />
          ) : list.length === 0 ? (
            <p className="muted">No agents{f ? ' match your filter' : ' — activate a package that declares agents'}.</p>
          ) : (
            list.map((a) => (
              <button
                key={a.agentId}
                className={`agent-card ${selected?.agentId === a.agentId ? 'active' : ''}`}
                onClick={() => pick(a)}
              >
                <div className="agent-card-head">
                  <strong>{a.name}</strong>
                  {a.riskTier && <span className={`posture-chip posture-${(a.riskTier || '').toLowerCase() === 'regulated' ? 'regulated' : 'elevated'}`}>{a.riskTier}</span>}
                </div>
                <span className="muted small">{a.description}</span>
                <span className="mono small" style={{ opacity: 0.7 }}>{a.agentId}</span>
              </button>
            ))
          )}
        </section>

        <section className="panel agents-detail">
          {!selected ? (
            <p className="muted">Select an agent to see its guardrails and ask it a question.</p>
          ) : (
            <>
              <h3>{selected.name}</h3>
              <p className="small">{selected.description}</p>
              <div className="agent-meta">
                {selected.owner && <span><span className="muted small">Owner</span> {selected.owner}</span>}
                {selected.independentValidator && <span><span className="muted small">Validator</span> {selected.independentValidator}</span>}
                {selected.contextSources && selected.contextSources.length > 0 && (
                  <span><span className="muted small">Sees</span> {selected.contextSources.join(', ')}</span>
                )}
              </div>
              {selected.prohibited && selected.prohibited.length > 0 && (
                <p className="muted small">Prohibited: {selected.prohibited.join('; ')}.</p>
              )}

              {selected.sampleQuestions && selected.sampleQuestions.length > 0 && (
                <div className="agent-samples">
                  {selected.sampleQuestions.map((q, i) => (
                    <button key={i} className="secondary small" onClick={() => { setQuestion(q); void ask(q) }}>{q}</button>
                  ))}
                </div>
              )}

              <textarea
                placeholder={`Ask ${selected.name}…`}
                value={question}
                onChange={(e) => setQuestion(e.target.value)}
                rows={3}
                style={{ width: '100%', marginTop: '0.5rem' }}
              />
              <button onClick={() => ask()} disabled={asking || !question.trim()}>{asking ? 'Asking…' : 'Ask'}</button>

              {askError && <p className="error small">{askError}</p>}
              {answer && (
                <div className="agent-answer">
                  <p className="muted small">Q: {answer.question}</p>
                  <p style={{ whiteSpace: 'pre-wrap' }}>{answer.answer}</p>
                  <p className="muted small">
                    {answer.provenance} · {answer.modelReference}
                    {selected.outputDisclaimer && <> · {selected.outputDisclaimer}</>}
                  </p>
                </div>
              )}
            </>
          )}
        </section>
      </div>
    </div>
  )
}
