import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api'

interface Connector {
  connectorId: string
  name: string
  sourceCategory: string
  ingestionMode: string
  schemaTemplateId: string
  reliabilityRating: number
  provenanceClass: string
  status: string
}

const EXAMPLES: Record<string, string> = {
  Opportunity:
    '{"opportunityId":"OPP-200","sourceType":"broker-network","reliabilityRating":0.9,"estimatedValue":1200000,"riskRating":0.3,"complianceRating":0.95,"readinessRating":0.8}',
  withdrawal: '{"transactionType":"withdrawal","ventureId":"V-001","amount":60000}',
}

/// The INPUT screen: everything that puts data into the pipeline lives here.
export function DataEntry({ tenant, user }: { tenant: string; user: string }) {
  const [recordType, setRecordType] = useState('Opportunity')
  const [payloadJson, setPayloadJson] = useState(EXAMPLES.Opportunity)
  const [connectors, setConnectors] = useState<Connector[]>([])
  const [connectorId, setConnectorId] = useState('manual-entry')
  const [rawPayload, setRawPayload] = useState('')
  const [docConnectorId, setDocConnectorId] = useState('manual-entry')
  const [docText, setDocText] = useState('')
  const [docFile, setDocFile] = useState<File | null>(null)
  const [docBusy, setDocBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const loadConnectors = useCallback(async () => {
    try {
      setConnectors(await api.get<Connector[]>('/api/connectors'))
    } catch {
      setConnectors([]) // connector config needs the Administrator identity
    }
  }, [])

  useEffect(() => {
    setMessage(null)
    setError(null)
    void loadConnectors()
  }, [loadConnectors, tenant, user])

  async function submitRecord() {
    setMessage(null)
    setError(null)
    try {
      const receipt = await api.post<{ transactionId: string; eventPublished: boolean; note: string | null }>(
        '/api/records',
        { recordType, payload: JSON.parse(payloadJson) },
      )
      setMessage(
        receipt.eventPublished
          ? `Record received and processing — see the outcome under Record Activity (id ${receipt.transactionId.slice(0, 8)}…).`
          : `Record stored, but not processed: ${receipt.note}`,
      )
    } catch (e) {
      if (e instanceof SyntaxError) setError(`Payload is not valid JSON: ${e.message}`)
      else setError(e instanceof ApiError ? e.message : String(e))
    }
  }

  async function ingest() {
    setMessage(null)
    setError(null)
    try {
      const result = await api.post<{ recordIds: string[]; warnings: string[] }>(
        `/api/connectors/${connectorId}/ingest`,
        { rawPayload },
      )
      setMessage(
        `Ingested ${result.recordIds.length} record(s) through '${connectorId}'.` +
          (result.warnings.length ? ` Warnings: ${result.warnings.join(' | ')}` : ''),
      )
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    }
  }

  async function extractDocument() {
    setMessage(null)
    setError(null)
    setDocBusy(true)
    try {
      let body: { fileName?: string; contentBase64?: string; text?: string }
      if (docFile) {
        const bytes = new Uint8Array(await docFile.arrayBuffer())
        let binary = ''
        bytes.forEach((b) => (binary += String.fromCharCode(b)))
        body = { fileName: docFile.name, contentBase64: btoa(binary) }
      } else {
        body = { fileName: 'pasted-text', text: docText }
      }
      const result = await api.post<{
        backend: string
        rawPayload: string
        ingestion: { recordIds: string[]; warnings: string[] }
      }>(`/api/connectors/${docConnectorId}/extract-document`, body)
      setMessage(
        `Document extracted via ${result.backend} — ${result.ingestion.recordIds.length} record(s) ingested through '${docConnectorId}'.` +
          (result.ingestion.warnings.length ? ` Warnings: ${result.ingestion.warnings.join(' | ')}` : ''),
      )
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    } finally {
      setDocBusy(false)
    }
  }

  return (
    <div>
      {message && <div className="banner banner-ok">{message}</div>}
      {error && <div className="banner banner-error">{error}</div>}

      <section className="panel">
        <h2>Submit a record <span className="state state-staged">INPUT</span></h2>
        <p className="muted small">
          The direct way in: pick what kind of record this is and fill its fields. It is stored
          immutably, then processed by your active packages. Submitted as the identity selected in
          the header.
        </p>
        <div className="form-row">
          <label>
            Record type
            <select
              value={recordType}
              onChange={(e) => {
                setRecordType(e.target.value)
                if (EXAMPLES[e.target.value]) setPayloadJson(EXAMPLES[e.target.value])
              }}
            >
              <option value="Opportunity">Opportunity (investment candidate)</option>
              <option value="withdrawal">withdrawal (treasury transaction)</option>
              <option value="OperationalEvent">OperationalEvent (site/equipment)</option>
            </select>
          </label>
          <button onClick={submitRecord}>Submit record</button>
        </div>
        <textarea rows={4} value={payloadJson} onChange={(e) => setPayloadJson(e.target.value)} />
      </section>

      <section className="panel">
        <h2>Ingest a raw source payload <span className="state state-staged">INPUT</span></h2>
        <p className="muted small">
          The connector way in: paste raw data exactly as a source system produces it (a bank
          statement line, a journal entry, a SiteView event). The connector's schema template maps
          it to a record and stamps source, provenance, and reliability automatically.
          {connectors.length === 0 && ' (Connector list requires the Administrator identity.)'}
        </p>
        <div className="form-row">
          <label>
            Connector
            <select value={connectorId} onChange={(e) => setConnectorId(e.target.value)}>
              {connectors.length === 0 && <option value="manual-entry">manual-entry</option>}
              {connectors.map((c) => (
                <option key={c.connectorId} value={c.connectorId}>
                  {c.name} ({c.sourceCategory}, maps via {c.schemaTemplateId})
                </option>
              ))}
            </select>
          </label>
          <button onClick={ingest} disabled={!rawPayload.trim()}>Ingest</button>
        </div>
        <textarea
          rows={5}
          placeholder='e.g. {"opportunityType":"RealEstateAcquisition","title":"Former Kmart site","estimatedValue":4200000}'
          value={rawPayload}
          onChange={(e) => setRawPayload(e.target.value)}
        />
      </section>

      <section className="panel">
        <h2>Extract a document <span className="state state-staged">INPUT</span></h2>
        <p className="muted small">
          The document way in: upload a PDF/text file or paste an email or invoice, and the
          extraction agent (<code>system:extractor</code>) turns it into the payload the selected
          connector expects — then normal ingestion runs. Templates with regex patterns extract
          deterministically; JSON templates use the AI backend (needs an Anthropic API key on the
          server).
        </p>
        <div className="form-row">
          <label>
            Connector
            <select value={docConnectorId} onChange={(e) => setDocConnectorId(e.target.value)}>
              {connectors.length === 0 && <option value="manual-entry">manual-entry</option>}
              {connectors.map((c) => (
                <option key={c.connectorId} value={c.connectorId}>
                  {c.name} ({c.sourceCategory}, maps via {c.schemaTemplateId})
                </option>
              ))}
            </select>
          </label>
          <label>
            File (.pdf, .txt, .eml)
            <input
              type="file"
              accept=".pdf,.txt,.eml,.md,.csv"
              onChange={(e) => setDocFile(e.target.files?.[0] ?? null)}
            />
          </label>
          <button onClick={extractDocument} disabled={docBusy || (!docFile && !docText.trim())}>
            {docBusy ? 'Extracting…' : 'Extract & ingest'}
          </button>
        </div>
        <textarea
          rows={5}
          placeholder="…or paste the document text here (an email, an invoice, a statement)."
          value={docText}
          onChange={(e) => setDocText(e.target.value)}
        />
      </section>
    </div>
  )
}
