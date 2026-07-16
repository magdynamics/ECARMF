import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api'
import { useToast } from './Toasts'

// Cases / projects — a cross-cutting grouping of the tenant's records, compared
// side by side. A record is filed under a case (via the caseId on submission);
// the full set of controls and KPIs still apply, so cases are comparable with
// the same measures as Period Analysis.

interface CaseMetrics {
  caseId: string
  name: string
  description?: string | null
  status: 'Open' | 'Closed'
  skills: string[]
  records: number
  rejected: number
  flagged: number
  controlsFired: number
  avgScore: number
  createdAt: string
}

function slugify(s: string) {
  return s.toLowerCase().trim().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 120)
}

export function Cases({ tenant, user }: { tenant: string; user: string }) {
  const toast = useToast()
  const [cases, setCases] = useState<CaseMetrics[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [caseId, setCaseId] = useState('')
  const [caseIdTouched, setCaseIdTouched] = useState(false)
  const [desc, setDesc] = useState('')

  const load = useCallback(async () => {
    setError(null)
    try { setCases(await api.get<CaseMetrics[]>('/api/cases')) }
    catch (e) { setError(e instanceof ApiError ? e.message : String(e)); setCases([]) }
  }, [])
  useEffect(() => { void load() }, [load, tenant, user])
  useEffect(() => { if (!caseIdTouched) setCaseId(slugify(name)) }, [name, caseIdTouched])

  const slugValid = /^[a-z0-9][a-z0-9-]{1,118}[a-z0-9]$/.test(caseId)

  async function create() {
    if (!name.trim() || !slugValid) return
    setBusy('create'); setError(null)
    try {
      await api.post('/api/cases', { caseId, name: name.trim(), description: desc.trim() || null })
      toast.success(`Case '${name.trim()}' opened.`)
      setName(''); setCaseId(''); setCaseIdTouched(false); setDesc('')
      await load()
    } catch (e) { setError(e instanceof ApiError ? e.message : String(e)) }
    finally { setBusy(null) }
  }

  async function toggleStatus(c: CaseMetrics) {
    setBusy(c.caseId); setError(null)
    try {
      await api.post(`/api/cases/${c.caseId}/status`, { status: c.status === 'Open' ? 'Closed' : 'Open' })
      toast.success(`Case '${c.name}' ${c.status === 'Open' ? 'closed' : 'reopened'}.`)
      await load()
    } catch (e) { setError(e instanceof ApiError ? e.message : String(e)) }
    finally { setBusy(null) }
  }

  const maxRec = Math.max(1, ...(cases ?? []).map((c) => c.records))

  return (
    <div>
      <section className="panel">
        <h2>Cases &amp; projects</h2>
        <p className="muted">
          Group this tenant&apos;s records into cases (or projects) and compare them side by side — same
          controls and KPIs, scoped per case. Records are filed under a case when submitted.
        </p>
        {error && <p className="error small">{error}</p>}
      </section>

      <section className="panel">
        <h3>Compare cases</h3>
        {cases === null ? <p className="muted">Loading…</p>
          : cases.length === 0 ? <p className="muted">No cases yet — open one below, then file records under it.</p>
          : (
            <table className="pd-table">
              <thead><tr><th>Case</th><th>Status</th><th>Records</th><th>Rejected</th><th>Flagged</th><th>Controls</th><th>Avg risk</th><th></th></tr></thead>
              <tbody>
                {cases.map((c) => (
                  <tr key={c.caseId}>
                    <td><strong>{c.name}</strong><div className="muted small">{c.caseId}{c.description ? ` · ${c.description}` : ''}</div></td>
                    <td><span className={`state state-${c.status === 'Open' ? 'active' : 'deactivated'}`}>{c.status}</span></td>
                    <td>
                      <div className="pd-bar-wrap">
                        <div className="pd-bar" style={{ width: `${(c.records / maxRec) * 100}%` }} />
                        <span>{c.records}</span>
                      </div>
                    </td>
                    <td className={c.rejected > 0 ? 'error-text' : 'muted'}>{c.rejected}</td>
                    <td>{c.flagged}</td>
                    <td className="muted">{c.controlsFired}</td>
                    <td>{c.avgScore > 0 ? c.avgScore.toLocaleString() : '—'}</td>
                    <td><button className="secondary small" onClick={() => toggleStatus(c)} disabled={busy === c.caseId}>
                      {busy === c.caseId ? '…' : c.status === 'Open' ? 'Close' : 'Reopen'}
                    </button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
      </section>

      <section className="panel">
        <h3>Open a case</h3>
        <div className="enroll-grid">
          <label>Name
            <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Q3 onboarding review" />
          </label>
          <label>Case id (slug)
            <input value={caseId} onChange={(e) => { setCaseId(slugify(e.target.value)); setCaseIdTouched(true) }} placeholder="q3-onboarding-review" />
            {caseId && !slugValid && <span className="error small">3–120 chars, lowercase letters/digits/hyphens.</span>}
          </label>
          <label className="enroll-wide">Description
            <input value={desc} onChange={(e) => setDesc(e.target.value)} placeholder="What this case covers" />
          </label>
        </div>
        <button onClick={create} disabled={!name.trim() || !slugValid || busy === 'create'}>
          {busy === 'create' ? 'Opening…' : 'Open case'}
        </button>
        <p className="muted small" style={{ marginTop: '0.5rem' }}>
          To file records under a case, include its case id when submitting a record.
        </p>
      </section>
    </div>
  )
}
