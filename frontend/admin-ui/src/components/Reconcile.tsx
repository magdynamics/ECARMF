import { useState } from 'react'
import { api, ApiError } from '../api'
import { Icon } from './Icon'

interface Source { documentId: string; fileName: string; subject: string | null; period: string | null; value: number }
interface Result {
  interpretation: string
  documentType: string
  field: string
  operation: string
  value: number
  documentsUsed: number
  sources: Source[]
}

const EXAMPLES = [
  'Review BOA account 123 and add all deposits',
  "Add employee John's W-2 wages for the last 3 years",
  'Sum all invoices for Oak Lawn',
  'How many bank statements do we have for Pulaski',
]

/// Reconciliation tasks: ask in plain English, the AI parses the request, and
/// the PLATFORM computes the total over extracted document data — every source
/// document shown, so the number is auditable, not a chatbot's guess.
export function Reconcile({ tenant, user }: { tenant: string; user: string }) {
  void user
  const [request, setRequest] = useState('')
  const [result, setResult] = useState<Result | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function run(q?: string) {
    const text = (q ?? request).trim()
    if (!text) return
    setBusy(true); setError(null); setResult(null)
    if (q) setRequest(q)
    try {
      setResult(await api.post<Result>('/api/reconciliation', { request: text }))
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    } finally {
      setBusy(false)
    }
  }

  const money = (n: number) => n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })

  return (
    <div>
      <section className="panel">
        <h2>Reconcile <span className="state state-approved">OUTPUT</span></h2>
        <p className="muted small">
          Ask a data task in plain English about tenant <strong>{tenant}</strong>'s documents. The AI
          works out <em>what</em> to fetch; the <strong>platform</strong> does the arithmetic over the
          extracted numbers and shows every source document behind the total — a figure you can audit,
          not one the AI made up. Documents must be filed through <strong>Document Triage</strong> first
          so their data is captured.
        </p>
        <textarea rows={2} placeholder="Review BOA account 123 and add all deposits…"
          value={request} onChange={(e) => setRequest(e.target.value)} />
        <div className="form-row" style={{ marginTop: '0.5rem', alignItems: 'center' }}>
          <button onClick={() => run()} disabled={busy || !request.trim()}>
            {busy ? <><span className="spinner-dot" /> Working…</> : 'Run task'}
          </button>
          {busy && <span className="muted small">the AI is parsing; the platform then computes</span>}
        </div>
        <p className="muted small">Try:{' '}
          {EXAMPLES.map((q, i) => (
            <button key={i} className="secondary" style={{ margin: '0 0.3rem 0.3rem 0' }} onClick={() => run(q)}>{q}</button>
          ))}
        </p>
        {error && <p className="error small">{error}</p>}
      </section>

      {result && (
        <section className="panel">
          <p className="muted small">{result.interpretation}</p>
          <div className="reconcile-total">
            <span className="muted small">{result.operation} of {result.field} · {result.documentType}</span>
            <div className="kpi-value" style={{ fontSize: '2rem' }}>
              {result.operation === 'count' ? result.value : money(result.value)}
            </div>
            <span className="muted small">from <strong>{result.documentsUsed}</strong> document(s) — computed by the platform</span>
          </div>
          {result.sources.length > 0 && (
            <table style={{ marginTop: '0.7rem' }}>
              <thead><tr><th>Source document</th><th>Subject</th><th>Period</th><th style={{ textAlign: 'right' }}>Value</th></tr></thead>
              <tbody>
                {result.sources.map((s) => (
                  <tr key={s.documentId}>
                    <td><Icon name="file-text" size={12} /> {s.fileName}</td>
                    <td className="small">{s.subject ?? '—'}</td>
                    <td className="small">{s.period ?? '—'}</td>
                    <td style={{ textAlign: 'right' }} className="mono">{result.operation === 'count' ? '1' : money(s.value)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
      )}
    </div>
  )
}
