import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api'
import { Icon } from './Icon'

interface OrgUnit { unitId: string; name: string; unitType: string; status: string }

interface Allocation {
  id: string
  documentId: string
  fileName: string
  recommendedUnitRef: string | null
  recommendedUnitName: string | null
  documentType: string | null
  confidence: number
  reasoning: string | null
  status: string
}

/// Bulk mixed-document triage: drop a pile of documents for a multi-entity
/// group, the AI recommends which business unit each belongs to, and a human
/// confirms or reassigns before it is filed. AI recommends, humans decide.
export function DocumentTriage({ tenant, user }: { tenant: string; user: string }) {
  const [units, setUnits] = useState<OrgUnit[]>([])
  const [queue, setQueue] = useState<Allocation[]>([])
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [progress, setProgress] = useState<{ done: number; total: number } | null>(null)
  const [choice, setChoice] = useState<Record<string, string>>({})

  const load = useCallback(async () => {
    try {
      setQueue(await api.get<Allocation[]>('/api/document-triage?status=Pending'))
      try { setUnits((await api.get<OrgUnit[]>('/api/org-units')).filter((u) => u.status !== 'Archived')) } catch { setUnits([]) }
      setError(null)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }, [])

  useEffect(() => { void load() }, [load, tenant, user])

  async function analyzeFiles(files: FileList | null) {
    if (!files || files.length === 0) return
    setBusy(true); setError(null); setMessage(null)
    setProgress({ done: 0, total: files.length })
    let ok = 0
    for (let i = 0; i < files.length; i++) {
      const f = files[i]
      try {
        const buf = new Uint8Array(await f.arrayBuffer())
        let binary = ''
        buf.forEach((b) => (binary += String.fromCharCode(b)))
        await api.post('/api/document-triage/analyze', { fileName: f.name, contentBase64: btoa(binary) })
        ok++
      } catch {
        // one bad file shouldn't stop the batch; it just won't appear in the queue
      }
      setProgress({ done: i + 1, total: files.length })
    }
    setBusy(false)
    setProgress(null)
    setMessage(`Analyzed ${ok} of ${files.length} document(s) — review the recommendations below.`)
    await load()
  }

  async function decide(a: Allocation, unitRef: string | null) {
    setError(null)
    try {
      await api.post(`/api/document-triage/${a.id}/decide`, { unitRef })
      setQueue((q) => q.filter((x) => x.id !== a.id))
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }

  // Confirm every high-confidence AI recommendation at once.
  async function confirmAllConfident() {
    const confident = queue.filter((a) => a.recommendedUnitRef && a.confidence >= 0.75)
    setBusy(true)
    for (const a of confident) await decide(a, a.recommendedUnitRef)
    setBusy(false)
    setMessage(`Filed ${confident.length} high-confidence document(s).`)
    await load()
  }

  const conf = (c: number) => `${Math.round(c * 100)}%`
  const confClass = (c: number) => (c >= 0.75 ? 'state-approved' : c >= 0.5 ? 'state-flagged' : 'state-rejected')

  return (
    <div>
      {message && <div className="banner banner-ok">{message}</div>}
      {error && <div className="banner banner-error">{error}</div>}

      <section className="panel">
        <h2>Document Triage <span className="state state-approved">OUTPUT</span></h2>
        <p className="muted small">
          Upload a pile of mixed documents for the whole group — invoices, leases, licenses, tax
          filings, EOBs. The AI reads each one and recommends which business unit (or the owner) it
          belongs to, and why. Nothing is filed until <strong>you</strong> confirm or reassign — the
          AI recommends, you decide. Once filed, each document carries its unit everywhere.
        </p>

        <div className="form-row" style={{ alignItems: 'center' }}>
          <label className="secondary" style={{ display: 'inline-flex', alignItems: 'center', gap: '0.4rem', cursor: 'pointer' }}>
            <Icon name="inbox-in" size={15} /> Upload documents
            <input type="file" multiple style={{ display: 'none' }} disabled={busy}
              onChange={(e) => { void analyzeFiles(e.target.files); e.target.value = '' }} />
          </label>
          {queue.length > 0 && (
            <button className="secondary" onClick={confirmAllConfident} disabled={busy}>
              Confirm all high-confidence ({queue.filter((a) => a.recommendedUnitRef && a.confidence >= 0.75).length})
            </button>
          )}
          {progress && <span className="muted small"><span className="spinner-dot" /> Analyzing {progress.done}/{progress.total}… (local model ~10–40s each)</span>}
        </div>
        <p className="muted small">
          Tip: for a very large batch (hundreds+), a cloud AI key (Setup → AI Backend) makes triage
          far faster than the local model.
        </p>
      </section>

      <section className="panel">
        <h3>Review queue <span className="muted small">{queue.length} pending</span></h3>
        {queue.length === 0 ? (
          <p className="muted">Nothing pending. Upload documents above to build the review queue.</p>
        ) : (
          <table>
            <thead>
              <tr><th>Document</th><th>AI says</th><th>Confidence</th><th>Why</th><th>File under</th><th></th></tr>
            </thead>
            <tbody>
              {queue.map((a) => (
                <tr key={a.id}>
                  <td><strong>{a.fileName}</strong>{a.documentType && <div className="muted small">{a.documentType}</div>}</td>
                  <td>{a.recommendedUnitName ?? <span className="muted">whole group / unsure</span>}</td>
                  <td><span className={`state ${confClass(a.confidence)}`}>{conf(a.confidence)}</span></td>
                  <td className="muted small" style={{ maxWidth: '18rem' }}>{a.reasoning}</td>
                  <td>
                    <select value={choice[a.id] ?? a.recommendedUnitRef ?? ''} onChange={(e) => setChoice({ ...choice, [a.id]: e.target.value })}>
                      <option value="">Whole group (tenant-wide)</option>
                      {units.map((u) => <option key={u.unitId} value={u.unitId}>{u.name} ({u.unitType})</option>)}
                    </select>
                  </td>
                  <td><button onClick={() => decide(a, (choice[a.id] ?? a.recommendedUnitRef) || null)} disabled={busy}>File</button></td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </div>
  )
}
