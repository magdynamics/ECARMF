import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api'

interface AiStatus {
  configured: boolean
  provider: string | null
  endpoint: string | null
  model: string | null
  apiKeyHint: string | null
  configuredBy: string | null
  updatedAt: string | null
}

/// Tenant-specific AI backend. Fully independent option: point at a local
/// OpenAI-compatible server (Ollama, LM Studio) running on this machine —
/// no external API, no key, nothing leaves the premises.
export function AiSettings({ tenant, user }: { tenant: string; user: string }) {
  const [status, setStatus] = useState<AiStatus | null>(null)
  const [provider, setProvider] = useState('local')
  const [apiKey, setApiKeyInput] = useState('')
  const [endpoint, setEndpoint] = useState('http://localhost:11434')
  const [model, setModel] = useState('')
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      setStatus(await api.get<AiStatus>('/api/settings/ai'))
      setError(null)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }, [])

  useEffect(() => {
    setMessage(null)
    void load()
  }, [load, tenant, user])

  async function save() {
    setError(null)
    setMessage(null)
    try {
      setStatus(await api.put<AiStatus>('/api/settings/ai', {
        provider,
        apiKey: apiKey || null,
        endpoint: provider === 'local' ? endpoint : null,
        model: model || null,
      }))
      setApiKeyInput('')
      setMessage(
        provider === 'local'
          ? 'On-premise AI backend configured — agents, briefs, and document extraction now run fully on your own machine.'
          : 'Anthropic backend configured for this tenant.',
      )
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }

  async function clear() {
    setError(null)
    try {
      setStatus(await api.delete<AiStatus>('/api/settings/ai'))
      setMessage('AI configuration removed; agents fall back to deterministic composers.')
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }

  return (
    <section className="panel">
      <h2>AI Backend <span className="state state-staged">SETUP</span></h2>
      <p className="muted small">
        Powers the Executive Advisor, specialized agents, and AI document extraction for this
        tenant. Two options: a <strong>local server on your own machine</strong> (Ollama, LM
        Studio — fully independent, no external API, no key, nothing leaves the premises) or an
        Anthropic API key (encrypted at rest, write-only). Without either, everything still works
        with deterministic fallbacks — rules, KPIs, benchmarks, and regex extraction never need AI.
      </p>

      {message && <div className="banner banner-ok">{message}</div>}
      {error && <div className="banner banner-error">{error}</div>}

      {status && (
        <p>
          Status:{' '}
          {status.configured ? (
            <>
              <span className="state state-approved">
                {status.provider === 'local' ? 'On-premise (local server)' : 'Anthropic API'}
              </span>{' '}
              <span className="muted small">
                {status.provider === 'local' ? `endpoint ${status.endpoint}` : `key ${status.apiKeyHint}`} · model{' '}
                {status.model ?? 'default'} · set by {status.configuredBy}{' '}
                {status.updatedAt ? `on ${new Date(status.updatedAt).toLocaleString()}` : ''}
              </span>
            </>
          ) : (
            <span className="state state-pending">Not configured — deterministic fallbacks active</span>
          )}
        </p>
      )}

      <div className="form-row">
        <label>Backend<select value={provider} onChange={(e) => setProvider(e.target.value)}>
          <option value="local">Local server (Ollama / LM Studio — independent)</option>
          <option value="anthropic">Anthropic API</option>
        </select></label>
        {provider === 'local' ? (
          <>
            <label>Endpoint<input placeholder="http://localhost:11434" value={endpoint}
              onChange={(e) => setEndpoint(e.target.value)} /></label>
            <label>Model<input placeholder="llama3.1" value={model}
              onChange={(e) => setModel(e.target.value)} /></label>
          </>
        ) : (
          <>
            <label>API key<input type="password" placeholder="sk-ant-…" value={apiKey}
              onChange={(e) => setApiKeyInput(e.target.value)} /></label>
            <label>Model (optional)<input placeholder="claude-opus-4-8" value={model}
              onChange={(e) => setModel(e.target.value)} /></label>
          </>
        )}
        <button onClick={save} disabled={provider === 'anthropic' ? !apiKey.trim() : !endpoint.trim()}>Save</button>
        {status?.configured && <button onClick={clear}>Remove configuration</button>}
      </div>
    </section>
  )
}
