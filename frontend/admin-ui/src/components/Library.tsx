import { useCallback, useEffect, useState } from 'react'
import { api, ApiError, getApiKey, getTenant, getUser } from '../api'

interface OrgUnit { unitId: string; name: string; unitType: string; status: string }

interface SourceDocument {
  unitRef: string | null
  id: string
  fileName: string
  mediaType: string
  sha256: string
  sizeBytes: number
  sourceId: string
  sourceCategory: string
  uploadedBy: string
  archivedAt: string
  extractionBackend: string | null
  schemaTemplateId: string | null
  recordIds: string[]
  metadata: Record<string, string>
}

/// The source library: every upload archived verbatim and indexed by
/// metadata — the evidence behind any record, searchable.
export function Library({ tenant, user }: { tenant: string; user: string }) {
  const [documents, setDocuments] = useState<SourceDocument[]>([])
  const [query, setQuery] = useState('')
  const [sourceId, setSourceId] = useState('')
  const [units, setUnits] = useState<OrgUnit[]>([])
  const [unitRef, setUnitRef] = useState('')
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      const params = new URLSearchParams()
      if (query.trim()) params.set('query', query.trim())
      if (sourceId.trim()) params.set('sourceId', sourceId.trim())
      if (unitRef) params.set('unitRef', unitRef)
      params.set('limit', '100')
      setDocuments(await api.get<SourceDocument[]>(`/api/library?${params}`))
      try { setUnits((await api.get<OrgUnit[]>('/api/org-units')).filter((u) => u.status !== 'Archived')) } catch { setUnits([]) }
      setError(null)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }, [query, sourceId, unitRef])

  useEffect(() => {
    void load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenant, user, unitRef])

  async function download(doc: SourceDocument) {
    const headers: Record<string, string> = {}
    const apiKey = getApiKey()
    if (apiKey) headers['X-Api-Key'] = apiKey
    else {
      headers['X-Tenant-Id'] = getTenant()
      headers['X-User-Id'] = getUser()
    }
    const response = await fetch(`/api/library/${doc.id}/content`, { headers })
    if (!response.ok) return
    const blob = await response.blob()
    const url = URL.createObjectURL(blob)
    const a = document2Anchor(url, doc.fileName)
    a.click()
    URL.revokeObjectURL(url)
  }

  function document2Anchor(url: string, name: string): HTMLAnchorElement {
    const a = document.createElement('a')
    a.href = url
    a.download = name
    return a
  }

  return (
    <section className="panel">
      <h2>Source Library <span className="state state-staged">OUTPUT</span></h2>
      <p className="muted small">
        Every upload — pasted forms, bank files, extracted PDFs, integration feeds — is archived
        verbatim (hash-sealed) and indexed by its metadata: source, category, uploader, template,
        and the records it produced. Rejected uploads are kept too: failed evidence is still
        evidence.
      </p>

      <div className="form-row">
        <label>Search<input placeholder="file name, source, hash, record id, metadata…"
          value={query} onChange={(e) => setQuery(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && load()} /></label>
        <label>Source<input placeholder="connector id" value={sourceId}
          onChange={(e) => setSourceId(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && load()} /></label>
        {units.length > 0 && (
          <label>Entity / location<select value={unitRef} onChange={(e) => setUnitRef(e.target.value)}>
            <option value="">All units</option>
            {units.map((u) => <option key={u.unitId} value={u.unitId}>{u.name} (+ tenant-wide)</option>)}
          </select></label>
        )}
        <button onClick={load}>Search</button>
      </div>

      <table>
        <thead>
          <tr><th>Archived</th><th>File</th><th>Source</th><th>Unit</th><th>By</th><th>Size</th><th>Extraction</th><th>Records</th><th>Accepted</th><th></th></tr>
        </thead>
        <tbody>
          {documents.map((d) => (
            <tr key={d.id}>
              <td className="small">{new Date(d.archivedAt).toLocaleString()}</td>
              <td><strong>{d.fileName}</strong> <span className="muted small">{d.mediaType}</span></td>
              <td className="mono small">{d.sourceId} ({d.sourceCategory})</td>
              <td className="small">{d.unitRef ?? <span className="muted">all</span>}</td>
              <td className="small">{d.uploadedBy}</td>
              <td className="small">{d.sizeBytes < 2048 ? `${d.sizeBytes} B` : `${Math.round(d.sizeBytes / 1024)} KB`}</td>
              <td className="small">{d.extractionBackend ?? '—'}</td>
              <td className="small">{d.recordIds.length}</td>
              <td>
                {d.metadata['accepted'] === 'False'
                  ? <span className="state state-rejected" title={d.metadata['errors'] ?? ''}>rejected</span>
                  : <span className="state state-approved">yes</span>}
              </td>
              <td><button onClick={() => download(d)}>Download</button></td>
            </tr>
          ))}
          {documents.length === 0 && !error && (
            <tr><td colSpan={9} className="muted">Nothing archived yet — every future upload lands here automatically.</td></tr>
          )}
        </tbody>
      </table>
      {error && <p className="error">{error}</p>}
    </section>
  )
}
