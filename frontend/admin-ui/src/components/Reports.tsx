import { useCallback, useEffect, useState } from 'react'
import { api, ApiError, getApiKey, getTenant, getUser } from '../api'

interface ReportDoc {
  id: string
  fileName: string
  sizeBytes: number
  archivedAt: string
  uploadedBy: string
  metadata: Record<string, string>
}

function authHeaders(): Record<string, string> {
  const key = getApiKey()
  return key
    ? { 'X-Api-Key': key }
    : { 'X-Tenant-Id': getTenant(), 'X-User-Id': getUser() }
}

/// The client deliverable: a self-contained report of activity, alerts,
/// scores, renewals, and usage for a period — archived like evidence,
/// generated monthly by schedule or on demand here.
export function Reports({ tenant, user }: { tenant: string; user: string }) {
  const [reports, setReports] = useState<ReportDoc[]>([])
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [emailIt, setEmailIt] = useState(false)

  const fail = (e: unknown) => setError(e instanceof ApiError ? e.message : String(e))

  const load = useCallback(async () => {
    try {
      setReports(await api.get<ReportDoc[]>('/api/reports'))
      setError(null)
    } catch (e) {
      fail(e)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load, tenant, user])

  async function generate() {
    setError(null)
    setMessage(null)
    setBusy(true)
    try {
      await api.post('/api/reports/generate', { periodStart: null, periodEnd: null, email: emailIt })
      setMessage(`Report generated for the month to date${emailIt ? ' and emailed to the executives on file' : ''}.`)
      await load()
    } catch (e) {
      fail(e)
    } finally {
      setBusy(false)
    }
  }

  // Open the archived HTML report in a new tab (identity headers required,
  // so fetch the bytes rather than linking directly).
  async function open(doc: ReportDoc) {
    try {
      const response = await fetch(`/api/library/${doc.id}/content`, { headers: authHeaders() })
      if (!response.ok) throw new ApiError(`${response.status} ${response.statusText}`, response.status, null)
      const blob = new Blob([await response.blob()], { type: 'text/html' })
      window.open(URL.createObjectURL(blob), '_blank')
    } catch (e) {
      fail(e)
    }
  }

  return (
    <div>
      {message && <div className="banner banner-ok">{message}</div>}
      {error && <div className="banner banner-error">{error}</div>}

      <section className="panel">
        <h2>Client reports <span className="state state-approved">OUTPUT</span></h2>
        <p className="muted small">
          One self-contained document per period: records processed, alerts raised, scores and
          KPIs, benchmark posture, upcoming renewals, open work, the advisor's brief, and metered
          usage — every figure traceable to the audit trail. A report for each finished month is
          generated automatically (and emailed when mail delivery is on); generate one on demand
          any time. Reports open in the browser and print to PDF.
        </p>

        <div className="form-row">
          <button onClick={generate} disabled={busy}>
            {busy ? 'Generating…' : 'Generate report now (month to date)'}
          </button>
          <label className="small">
            <input type="checkbox" checked={emailIt} onChange={(e) => setEmailIt(e.target.checked)} />{' '}
            also email it
          </label>
        </div>

        <div className="form-row">
          <label className="small">
            Audit export (regulator-ready CSV of the append-only trail, last 12 months)
          </label>
          <button className="secondary" onClick={async () => {
            try {
              const response = await fetch('/api/audit/export', { headers: authHeaders() })
              if (!response.ok) throw new ApiError(`${response.status} ${response.statusText}`, response.status, null)
              const blob = await response.blob()
              const url = URL.createObjectURL(blob)
              const a = document.createElement('a')
              a.href = url
              a.download = `audit-${getTenant() || 'tenant'}.csv`
              a.click()
              URL.revokeObjectURL(url)
            } catch (e) {
              setError(e instanceof ApiError ? e.message : String(e))
            }
          }}>Download audit CSV</button>
        </div>

        <table>
          <thead>
            <tr><th>Report</th><th>Period</th><th>Generated</th><th>By</th><th>Size</th><th></th></tr>
          </thead>
          <tbody>
            {reports.map((r) => (
              <tr key={r.id}>
                <td><strong>{r.fileName}</strong></td>
                <td className="small">
                  {r.metadata.periodStart?.slice(0, 10)} → {r.metadata.periodEnd?.slice(0, 10)}
                </td>
                <td className="small">{new Date(r.archivedAt).toLocaleString()}</td>
                <td className="small">{r.uploadedBy}</td>
                <td className="small">{(r.sizeBytes / 1024).toFixed(1)} KB</td>
                <td><button className="secondary" onClick={() => open(r)}>Open</button></td>
              </tr>
            ))}
            {reports.length === 0 && (
              <tr><td colSpan={6} className="muted">
                No reports yet — generate the first one above, or wait for the monthly cycle.
              </td></tr>
            )}
          </tbody>
        </table>
      </section>
    </div>
  )
}
