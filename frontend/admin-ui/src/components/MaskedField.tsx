import { useState } from 'react'
import { api } from '../api'

// Masked-Field / Audit-Logged PHI pattern (ECARMF-ADR-UIUX-001 Phase 1, §2.4).
// A PHI-touching value carries a persistent badge, is masked by default, and
// requires an explicit reveal. Every reveal is audit-logged server-side (POST
// /api/phi/reveal) so access is attributable — the client cannot quietly
// un-mask without a record — and the PRIOR viewer is surfaced as
// "last viewed by X at Y".

interface RevealResponse {
  fieldKey: string
  revealedBy: string
  revealedAt: string
  previousViewedBy: string | null
  previousViewedAt: string | null
}

export function MaskedField({
  value,
  fieldKey,
  subjectRef,
  screen,
  mask = '••••••••',
}: {
  /** The PHI value, shown only after an explicit, audited reveal. */
  value: string
  /** Stable key identifying which field this is (e.g. "member.ssn"). Audited. */
  fieldKey: string
  /** Which record/subject the value belongs to (e.g. a claim id). Audited. */
  subjectRef?: string
  /** Screen the reveal happened on, for the audit detail. */
  screen?: string
  mask?: string
}) {
  const [revealed, setRevealed] = useState(false)
  const [lastViewed, setLastViewed] = useState<{ by: string; at: string } | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function reveal() {
    setBusy(true)
    setError(null)
    try {
      const r = await api.post<RevealResponse>('/api/phi/reveal', { fieldKey, subjectRef, screen })
      setRevealed(true)
      if (r.previousViewedBy && r.previousViewedAt) {
        setLastViewed({ by: r.previousViewedBy, at: r.previousViewedAt })
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Reveal failed')
    } finally {
      setBusy(false)
    }
  }

  return (
    <span className="phi-field">
      <span className="phi-badge" title="Protected health information — every reveal is audit-logged.">PHI</span>
      {revealed ? (
        <span className="phi-value mono">{value}</span>
      ) : (
        <span className="phi-masked" aria-label="Masked protected value">{mask}</span>
      )}
      <button
        className="phi-toggle"
        onClick={() => (revealed ? setRevealed(false) : reveal())}
        disabled={busy}
        aria-pressed={revealed}
      >
        {busy ? '…' : revealed ? 'Hide' : 'Reveal'}
      </button>
      {revealed && lastViewed && (
        <span className="phi-lastviewed muted small">
          last viewed by {lastViewed.by} at {new Date(lastViewed.at).toLocaleString()}
        </span>
      )}
      {error && <span className="error small">{error}</span>}
    </span>
  )
}
