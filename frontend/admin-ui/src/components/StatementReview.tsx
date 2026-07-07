import { useCallback, useEffect, useRef, useState } from 'react'
import { api, ApiError } from '../api'

interface LineItem {
  label: string
  value: number
  confidenceScore: number
  provenance: string
  sourceText: string | null
}

interface Statement {
  id: string
  statementType: string
  subjectEntity: string
  period: string
  extractionMethod: string
  templateId: string
  reviewThreshold: number
  status: string
  lineItems: LineItem[]
  createdBy: string
  createdAt: string
  reviewedBy: string | null
  reviewComment: string | null
  analyzedAt: string | null
}

/// The human gate for AI-extracted financial statements: flagged
/// low-confidence values are shown with the exact source text the model
/// read them from; the reviewer corrects and approves (releasing the
/// statement into ratio analysis) or rejects. Nothing below the confidence
/// threshold reaches a score or a capital recommendation without a human.
export function StatementReview({ tenant, user }: { tenant: string; user: string }) {
  const [statements, setStatements] = useState<Statement[]>([])
  const [statusFilter, setStatusFilter] = useState('PendingReview')
  const [framing, setFraming] = useState('')
  const [selected, setSelected] = useState<Statement | null>(null)
  const [corrections, setCorrections] = useState<Record<string, string>>({})
  const [comment, setComment] = useState('')
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const fail = (e: unknown) => setError(e instanceof ApiError ? e.message : String(e))

  const load = useCallback(async () => {
    try {
      const res = await api.get<{ statements: Statement[]; framing: string }>(
        `/api/financial-statements${statusFilter ? `?status=${statusFilter}` : ''}`)
      setStatements(res.statements)
      setFraming(res.framing)
      setError(null)
    } catch (e) {
      fail(e)
    }
  }, [statusFilter])

  useEffect(() => {
    setSelected(null)
    setCorrections({})
    void load()
  }, [load, tenant, user])

  const detailRef = useRef<HTMLElement | null>(null)
  useEffect(() => {
    if (!selected) return
    const jump = () => detailRef.current?.scrollIntoView({ behavior: 'auto', block: 'start' })
    requestAnimationFrame(jump)
    const settle = setTimeout(jump, 400)
    return () => clearTimeout(settle)
  }, [selected])

  function open(statement: Statement) {
    setSelected(statement)
    setCorrections({})
    setComment('')
    setMessage(null)
  }

  async function review(action: 'Approve' | 'Reject') {
    if (!selected) return
    setError(null)
    try {
      const body = {
        action,
        comment: comment || null,
        corrections: Object.entries(corrections)
          .filter(([, v]) => v.trim() !== '' && !isNaN(Number(v)))
          .map(([label, v]) => ({ label, value: Number(v) })),
      }
      await api.post(`/api/financial-statements/${selected.id}/review`, body)
      setMessage(`Statement ${action.toLowerCase()}d.${action === 'Approve' ? ' Ratios will compute from the released record.' : ''}`)
      setSelected(null)
      await load()
    } catch (e) {
      fail(e)
    }
  }

  const isLow = (item: LineItem, s: Statement) => item.confidenceScore < s.reviewThreshold

  return (
    <div>
      <section className="panel">
        <h2>Statement Review <span className="pill">INPUT</span></h2>
        <p className="muted">
          AI-extracted financial statements wait here when any value's confidence falls below the
          template threshold. A human corrects and approves (releasing the statement into ratio
          analysis) or rejects — a misread figure never silently reaches a risk score.
        </p>
        <p className="muted small">{framing}</p>

        <div className="form-row">
          {['PendingReview', 'Approved', 'Rejected', ''].map((s) => (
            <button
              key={s || 'all'}
              className={statusFilter === s ? '' : 'secondary'}
              onClick={() => setStatusFilter(s)}
            >
              {s === '' ? 'All' : s}
            </button>
          ))}
        </div>

        {error && <p className="error">{error}</p>}
        {message && <p className="success">{message}</p>}

        {statements.length === 0 ? (
          <p className="muted">
            No {statusFilter === '' ? '' : `${statusFilter} `}statements. Extracted statements arrive
            via POST /api/financial-statements/extract (Data Entry integrations or the API).
          </p>
        ) : (
          <table>
            <thead>
              <tr><th>Subject</th><th>Type</th><th>Period</th><th>Fields</th><th>Flagged</th><th>Status</th><th>Created</th><th></th></tr>
            </thead>
            <tbody>
              {statements.map((s) => {
                const flagged = s.lineItems.filter((l) => isLow(l, s)).length
                return (
                  <tr key={s.id} className={selected?.id === s.id ? 'selected' : ''}>
                    <td className="mono">{s.subjectEntity}</td>
                    <td>{s.statementType}</td>
                    <td>{s.period}</td>
                    <td>{s.lineItems.length}</td>
                    <td>{flagged > 0 ? <span className="state state-flagged">{flagged}</span> : '—'}</td>
                    <td><span className={`state ${s.status === 'Approved' ? 'state-approved' : s.status === 'Rejected' ? 'state-rejected' : 'state-flagged'}`}>{s.status}</span></td>
                    <td className="muted small">{new Date(s.createdAt).toLocaleString()}</td>
                    <td><button className="secondary" onClick={() => open(s)}>{s.status === 'PendingReview' ? 'Review' : 'View'}</button></td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        )}
      </section>

      {selected && (
        <section className="panel" ref={detailRef}>
          <h2>
            {selected.subjectEntity} — {selected.statementType} ({selected.period}){' '}
            <span className="muted small">threshold {(selected.reviewThreshold * 100).toFixed(0)}%</span>
          </h2>
          <table>
            <thead>
              <tr><th>Line item</th><th>Extracted value</th><th>Confidence</th><th>Provenance</th><th>Read from</th>
                {selected.status === 'PendingReview' && <th>Correction</th>}</tr>
            </thead>
            <tbody>
              {selected.lineItems.map((item) => (
                <tr key={item.label} style={isLow(item, selected) ? { outline: '1px solid #b4232355' } : undefined}>
                  <td className="mono">{item.label}</td>
                  <td>{item.value.toLocaleString()}</td>
                  <td>
                    <span className={`state ${isLow(item, selected) ? 'state-rejected' : 'state-approved'}`}>
                      {(item.confidenceScore * 100).toFixed(0)}%
                    </span>
                  </td>
                  <td className="muted small">{item.provenance}</td>
                  <td className="muted small">{item.sourceText ?? '—'}</td>
                  {selected.status === 'PendingReview' && (
                    <td>
                      <input
                        style={{ width: '8rem' }}
                        placeholder={isLow(item, selected) ? 'required to approve' : 'optional'}
                        value={corrections[item.label] ?? ''}
                        onChange={(e) => setCorrections({ ...corrections, [item.label]: e.target.value })}
                      />
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>

          {selected.status === 'PendingReview' ? (
            <>
              <p className="muted small">
                Flagged rows must be corrected (verify against the archived source document) before
                approval. Corrections are recorded as HumanEntered at full confidence.
              </p>
              <div className="form-row">
                <label>Comment<input value={comment} onChange={(e) => setComment(e.target.value)}
                  placeholder="Verified against the source scan…" /></label>
                <button onClick={() => review('Approve')}>Approve &amp; release for analysis</button>
                <button className="secondary" onClick={() => review('Reject')}>Reject</button>
              </div>
            </>
          ) : (
            <p className="muted small">
              {selected.status} by {selected.reviewedBy ?? '—'}
              {selected.reviewComment ? ` — "${selected.reviewComment}"` : ''}
              {selected.analyzedAt ? ` · released for analysis ${new Date(selected.analyzedAt).toLocaleString()}` : ''}
            </p>
          )}
        </section>
      )}
    </div>
  )
}
