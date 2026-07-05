import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api'

interface AiStatus {
  configured: boolean
  model: string | null
  apiKeyHint: string | null
  configuredBy: string | null
  updatedAt: string | null
}

/// Tenant-specific AI backend: each client brings its own Anthropic API key,
/// so AI usage, billing, and behavior never cross tenants.
export function AiSettings({ tenant, user }: { tenant: string; user: string }) {
  const [status, setStatus] = useState<AiStatus | null>(null)
  const [apiKey, setApiKeyInput] = useState('')
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
      setStatus(await api.put<AiStatus>('/api/settings/ai', { apiKey, model: model || null }))
      setApiKeyInput('')
      setMessage('AI backend configured for this tenant. The Advisor and document extraction now use it.')
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }

  async function clear() {
    setError(null)
    try {
      setStatus(await api.delete<AiStatus>('/api/settings/ai'))
      setMessage('Credential removed; agents fall back to deterministic composers.')
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }

  return (
    <section className="panel">
      <h2>AI Backend <span className="state state-staged">SETUP</span></h2>
      <p className="muted small">
        This tenant's own Anthropic API key powers the Executive Advisor and AI document
        extraction. The key is write-only: stored encrypted, shown never — only the masked hint
        below. Without a key, both agents use their deterministic fallbacks.
      </p>

      {message && <div className="banner banner-ok">{message}</div>}
      {error && <div className="banner banner-error">{error}</div>}

      {status && (
        <p>
          Status:{' '}
          {status.configured ? (
            <>
              <span className="state state-approved">Configured</span>{' '}
              <span className="muted small">
                key {status.apiKeyHint} · model {status.model ?? 'claude-opus-4-8 (default)'} · set by{' '}
                {status.configuredBy} {status.updatedAt ? `on ${new Date(status.updatedAt).toLocaleString()}` : ''}
              </span>
            </>
          ) : (
            <span className="state state-pending">Not configured — deterministic fallbacks active</span>
          )}
        </p>
      )}

      <div className="form-row">
        <label>Anthropic API key<input type="password" placeholder="sk-ant-…" value={apiKey}
          onChange={(e) => setApiKeyInput(e.target.value)} /></label>
        <label>Model (optional)<input placeholder="claude-opus-4-8" value={model}
          onChange={(e) => setModel(e.target.value)} /></label>
        <button onClick={save} disabled={!apiKey.trim()}>Save</button>
        {status?.configured && <button onClick={clear}>Remove credential</button>}
      </div>
    </section>
  )
}
