import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api'
import type { ActivityItem } from '../types'

const POLL_MS = 5000
const OUTCOMES = ['Approved', 'Rejected', 'Flagged', 'Accept', 'Hold', 'Escalate', 'AuditFurther', 'AMLEscalated', 'JournalHeld', 'ControlDeficiencyLogged', 'AccreditationReviewRequired']

interface SearchResult {
  total: number
  page: number
  pageSize: number
  items: ActivityItem[]
}

/// The record activity OUTPUT screen, organized by metadata and built for
/// thousands of records: filter by what the record is, what was decided,
/// who submitted it, and when — with paging and free-text search.
export function RecordActivity({ tenant, user }: { tenant: string; user: string }) {
  const [result, setResult] = useState<SearchResult>({ total: 0, page: 1, pageSize: 25, items: [] })
  const [recordTypes, setRecordTypes] = useState<string[]>([])
  const [error, setError] = useState<string | null>(null)

  const [recordType, setRecordType] = useState('')
  const [outcome, setOutcome] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)

  const refresh = useCallback(async () => {
    try {
      const params = new URLSearchParams({ page: String(page), pageSize: '25' })
      if (recordType) params.set('recordType', recordType)
      if (outcome) params.set('outcome', outcome)
      if (search.trim()) params.set('search', search.trim())
      setResult(await api.get<SearchResult>(`/api/records/search?${params}`))
      setRecordTypes(await api.get<string[]>('/api/records/types'))
      setError(null)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }, [page, recordType, outcome, search])

  useEffect(() => {
    void refresh()
    const timer = setInterval(() => void refresh(), POLL_MS)
    return () => clearInterval(timer)
  }, [refresh, tenant, user])

  useEffect(() => {
    setPage(1)
  }, [recordType, outcome, search, tenant])

  async function decide(recordId: string, verdict: 'Approve' | 'Reject') {
    setError(null)
    try {
      await api.post(`/api/records/${recordId}/approvals`, { verdict, comment: null })
      await refresh()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }

  const totalPages = Math.max(1, Math.ceil(result.total / result.pageSize))

  return (
    <div>
      {error && <div className="banner banner-error">{error}</div>}
      <section className="panel">
        <h2>
          Record Activity <span className="state state-approved">OUTPUT</span>{' '}
          <span className="muted small">{result.total} record(s) match</span>
        </h2>
        <p className="muted small">
          Every record that entered the pipeline, organized by its metadata: what it is, what the
          kernel decided, which rule decided it, and why. Flagged items show Approve/Reject
          (approver must differ from submitter).
        </p>

        <div className="form-row">
          <label>Record type<select value={recordType} onChange={(e) => setRecordType(e.target.value)}>
            <option value="">all types</option>
            {recordTypes.map((t) => <option key={t} value={t}>{t}</option>)}
          </select></label>
          <label>Outcome<select value={outcome} onChange={(e) => setOutcome(e.target.value)}>
            <option value="">all outcomes</option>
            {OUTCOMES.map((o) => <option key={o} value={o}>{o}</option>)}
          </select></label>
          <label className="grow">Search<input placeholder="free text over type, submitter, payload…"
            value={search} onChange={(e) => setSearch(e.target.value)} /></label>
          <label>Page
            <span className="small" style={{ display: 'flex', gap: '0.3rem', alignItems: 'center' }}>
              <button onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page <= 1}>‹</button>
              {result.page} / {totalPages}
              <button onClick={() => setPage((p) => Math.min(totalPages, p + 1))} disabled={page >= totalPages}>›</button>
            </span>
          </label>
        </div>

        <table>
          <thead>
            <tr>
              <th>Received</th>
              <th>Record</th>
              <th>Payload</th>
              <th>Outcome</th>
              <th>Fired rule / reason</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {result.items.length === 0 && (
              <tr><td colSpan={6} className="muted">No records match these filters.</td></tr>
            )}
            {result.items.map((t) => {
              const last = t.outcomes[t.outcomes.length - 1]
              const isFlagged = last?.outcome?.toLowerCase() === 'flagged'
              return (
                <tr key={t.recordId}>
                  <td className="small">{new Date(t.receivedAt).toLocaleString()}</td>
                  <td>
                    <strong>{t.recordType}</strong>
                    <div className="muted small">{t.submittedBy}</div>
                    <div className="muted small mono">{t.recordId.slice(0, 8)}…</div>
                  </td>
                  <td className="small mono">
                    {Object.entries(t.payload)
                      .filter(([k]) => !['sourceId', 'sourceCategory', 'sourceType', 'provenance', 'reliabilityRating', 'ingestedAt', 'schemaTemplateId', 'schemaTemplateVersion', 'recordType', 'transactionType', 'submittedBy'].includes(k))
                      .slice(0, 6)
                      .map(([k, v]) => `${k}=${v}`)
                      .join(' ')}
                  </td>
                  <td>
                    {last ? (
                      <span className={`state state-${last.outcome.toLowerCase()}`}>{last.outcome}</span>
                    ) : (
                      <span className="state state-pending">Pending</span>
                    )}
                  </td>
                  <td className="small">
                    {last ? (
                      <>
                        {last.ruleId ? (
                          <div>
                            <code>{last.ruleId}</code>{' '}
                            <span className="muted">({last.packageId} v{last.packageVersion})</span>
                          </div>
                        ) : (
                          <div className="muted">default kernel policy</div>
                        )}
                        <div className="muted">{last.reason}</div>
                      </>
                    ) : (
                      <span className="muted">awaiting processing…</span>
                    )}
                  </td>
                  <td>
                    {isFlagged && (
                      <div className="approval-actions">
                        <button onClick={() => decide(t.recordId, 'Approve')}>Approve</button>
                        <button className="secondary" onClick={() => decide(t.recordId, 'Reject')}>Reject</button>
                      </div>
                    )}
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </section>
    </div>
  )
}
