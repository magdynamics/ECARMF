import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api'
import { Icon } from './Icon'

interface ReferenceSource {
  id: string
  title: string
  url: string
  issuer: string | null
  jurisdiction: string | null
  category: string
  description: string | null
  addedBy: string
  addedAt: string
}

const CATEGORIES = ['StateRegistry', 'PublicRecords', 'Regulator', 'TaxAuthority', 'IndustryStandard', 'ReferenceSource']

/// Reference Sources — the ad-hoc "paste a link" door. An authoritative
/// external URL (a state registry, a public-records portal, a regulator's
/// site) becomes a knowledge asset agents can cite, without authoring a
/// package's JSON manifest.
export function ReferenceSources({ tenant, user }: { tenant: string; user: string }) {
  const [sources, setSources] = useState<ReferenceSource[]>([])
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [form, setForm] = useState({
    title: '', url: '', issuer: '', jurisdiction: '', category: 'StateRegistry', description: '',
  })

  const load = useCallback(async () => {
    try {
      setSources(await api.get<ReferenceSource[]>('/api/reference-sources'))
      setError(null)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }, [])

  useEffect(() => { void load() }, [load, tenant, user])

  async function add() {
    setError(null)
    setMessage(null)
    try {
      await api.post('/api/reference-sources', {
        title: form.title,
        url: form.url,
        issuer: form.issuer || null,
        jurisdiction: form.jurisdiction || null,
        category: form.category,
        description: form.description || null,
      })
      setMessage(`Reference source '${form.title}' added — agents can now cite it.`)
      setForm({ title: '', url: '', issuer: '', jurisdiction: '', category: 'StateRegistry', description: '' })
      await load()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }

  async function remove(id: string) {
    setError(null)
    try {
      await api.delete(`/api/reference-sources/${id}`)
      await load()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }

  return (
    <div>
      {message && <div className="banner banner-ok">{message}</div>}
      {error && <div className="banner banner-error">{error}</div>}

      <section className="panel">
        <h2>Reference Sources <span className="state state-staged">SETUP</span></h2>
        <p className="muted small">
          Register an authoritative external link — a state registry, a public-records portal, a
          regulator's site — so it becomes a knowledge asset your AI agents can <strong>cite</strong> and
          a person can open. Unlike a knowledge document (which holds text the model reads), a
          reference source is a <strong>pointer</strong>: the platform surfaces it as an authoritative
          place to consult, but does not fetch it live. This is the "paste a link" way to add a source
          without authoring a package.
        </p>

        <table>
          <thead>
            <tr><th>Source</th><th>Link</th><th>Issuer</th><th>Jurisdiction</th><th>Category</th><th></th></tr>
          </thead>
          <tbody>
            {sources.map((s) => (
              <tr key={s.id}>
                <td><strong>{s.title}</strong>{s.description && <div className="muted small">{s.description}</div>}</td>
                <td><a href={s.url} target="_blank" rel="noreferrer">{new URL(s.url).hostname} <Icon name="arrow-right" size={11} /></a></td>
                <td className="small">{s.issuer ?? <span className="muted">—</span>}</td>
                <td className="small">{s.jurisdiction ?? <span className="muted">neutral</span>}</td>
                <td><span className="state state-staged">{s.category}</span></td>
                <td><button className="secondary small" onClick={() => remove(s.id)}>Remove</button></td>
              </tr>
            ))}
            {sources.length === 0 && <tr><td colSpan={6} className="muted">No reference sources yet — add one below.</td></tr>}
          </tbody>
        </table>

        <h3>Add a reference source</h3>
        <div className="form-row">
          <label>Title<input placeholder="Illinois SOS — Business Entity Search" value={form.title}
            onChange={(e) => setForm({ ...form, title: e.target.value })} /></label>
          <label>Link (URL)<input placeholder="https://apps.ilsos.gov/businessentitysearch/" value={form.url}
            onChange={(e) => setForm({ ...form, url: e.target.value })} /></label>
          <label>Issuer<input placeholder="Illinois Secretary of State" value={form.issuer}
            onChange={(e) => setForm({ ...form, issuer: e.target.value })} /></label>
          <label>Jurisdiction<input placeholder="Illinois" value={form.jurisdiction}
            onChange={(e) => setForm({ ...form, jurisdiction: e.target.value })} /></label>
          <label>Category<select value={form.category} onChange={(e) => setForm({ ...form, category: e.target.value })}>
            {CATEGORIES.map((c) => <option key={c} value={c}>{c}</option>)}
          </select></label>
        </div>
        <label>What it's for (agents relay this to a human)
          <input placeholder="Verify entity status, registered agent, and officers before onboarding or outreach."
            value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} /></label>
        <div className="form-row" style={{ marginTop: '0.5rem' }}>
          <button onClick={add} disabled={!form.title.trim() || !form.url.trim()}>Add reference source</button>
        </div>
      </section>
    </div>
  )
}
