import { useCallback, useEffect, useMemo, useState } from 'react'
import { api, ApiError } from '../api'
import type { ScoreRecord } from '../types'

// Risk treatment — turns the risk heatmap into managed risk. Each risk (from
// its risk-tagged score) can be put under treatment: an owner, a strategy
// (mitigate/accept/transfer/avoid), a status, a plan, and a residual rating
// after mitigation. Closes the loop between seeing risk and managing it.

interface RiskTreatment {
  id: string; riskKey: string; title: string; domain: string
  inherentSeverity: number; inherentLikelihood: number
  owner?: string | null; strategy: string; status: string; mitigationPlan?: string | null
  residualSeverity?: number | null; residualLikelihood?: number | null
  targetDate?: string | null; linkedActionRef?: string | null
}
interface RiskItem { riskKey: string; title: string; domain: string; severity: number; likelihood: number }

const STRATEGIES = ['Mitigate', 'Accept', 'Transfer', 'Avoid']
const STATUSES = ['Identified', 'InTreatment', 'Mitigated', 'Accepted', 'Closed']
const clamp = (n: number) => Math.max(1, Math.min(5, n))

function sevLike(s: ScoreRecord): { severity: number; likelihood: number } {
  const sev = Number(s.metadata?.severityValue ?? s.metadata?.severity)
  const like = Number(s.metadata?.likelihood)
  if (Number.isFinite(sev) && sev > 0 && Number.isFinite(like) && like > 0) return { severity: clamp(sev), likelihood: clamp(like) }
  const v = Number(s.value) || 1
  const severity = clamp(Math.round(Math.sqrt(Math.max(1, v))))
  return { severity, likelihood: clamp(Math.round(v / Math.max(1, severity))) }
}

export function RiskTreatments({ tenant, user }: { tenant: string; user: string }) {
  const [treatments, setTreatments] = useState<RiskTreatment[] | null>(null)
  const [risks, setRisks] = useState<RiskItem[]>([])
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [editing, setEditing] = useState<RiskTreatment | null>(null)

  const load = useCallback(async () => {
    setError(null)
    try {
      const [tr, scores] = await Promise.all([
        api.get<RiskTreatment[]>('/api/risk/treatments'),
        api.get<ScoreRecord[]>('/api/scores?riskOnly=true&limit=3000'),
      ])
      setTreatments(tr)
      // One risk = latest score per subject (scores are newest-first).
      const seen = new Set<string>()
      const items: RiskItem[] = []
      for (const s of scores) {
        const subj = s.subjectId ?? ''
        if (!subj || seen.has(subj)) continue
        seen.add(subj)
        const domain = s.riskType ?? 'General'
        const title = subj.includes('@') ? subj.split('@').slice(1).join('@') : subj
        const { severity, likelihood } = sevLike(s)
        items.push({ riskKey: `${domain}:${subj}`, title, domain, severity, likelihood })
      }
      setRisks(items)
    } catch (e) { setError(e instanceof ApiError ? e.message : String(e)); setTreatments([]) }
  }, [])
  useEffect(() => { void load() }, [load, tenant, user])

  const treatedKeys = useMemo(() => new Set((treatments ?? []).map((t) => t.riskKey)), [treatments])
  const untreated = useMemo(() =>
    risks.filter((r) => !treatedKeys.has(r.riskKey)).sort((a, b) => b.severity * b.likelihood - a.severity * a.likelihood),
    [risks, treatedKeys])

  async function manage(r: RiskItem) {
    setBusy(r.riskKey); setError(null)
    try {
      await api.post('/api/risk/treatments', {
        riskKey: r.riskKey, title: r.title, domain: r.domain,
        inherentSeverity: r.severity, inherentLikelihood: r.likelihood, strategy: 'Mitigate',
      })
      await load()
    } catch (e) { setError(e instanceof ApiError ? e.message : String(e)) }
    finally { setBusy(null) }
  }

  async function remediate() {
    if (!editing) return
    setBusy(editing.id); setError(null)
    try {
      const r = await api.post<{ treatment: RiskTreatment; actionId: string }>(`/api/risk/treatments/${editing.id}/remediate`)
      setEditing({ ...editing, ...r.treatment })
      await load()
    } catch (e) { setError(e instanceof ApiError ? e.message : String(e)) }
    finally { setBusy(null) }
  }

  async function save() {
    if (!editing) return
    setBusy(editing.id); setError(null)
    try {
      await api.put(`/api/risk/treatments/${editing.id}`, {
        owner: editing.owner ?? '', strategy: editing.strategy, status: editing.status,
        mitigationPlan: editing.mitigationPlan ?? '',
        residualSeverity: editing.residualSeverity ?? null, residualLikelihood: editing.residualLikelihood ?? null,
        targetDate: editing.targetDate || null, linkedActionRef: editing.linkedActionRef ?? '',
      })
      setEditing(null)
      await load()
    } catch (e) { setError(e instanceof ApiError ? e.message : String(e)) }
    finally { setBusy(null) }
  }

  const byStatus = useMemo(() => {
    const m: Record<string, number> = {}
    for (const t of treatments ?? []) m[t.status] = (m[t.status] ?? 0) + 1
    return m
  }, [treatments])

  return (
    <div>
      <section className="panel">
        <h2>Risk treatment</h2>
        <p className="muted">
          Turn identified risks into managed risk: assign an owner and strategy, track status and a
          mitigation plan, and record the residual rating once treated.
        </p>
        {treatments && (
          <p className="small">
            <strong>{treatments.length}</strong> under treatment · {untreated.length} untreated ·{' '}
            {STATUSES.filter((s) => byStatus[s]).map((s) => `${byStatus[s]} ${s}`).join(' · ') || 'none yet'}
          </p>
        )}
        {error && <p className="error small">{error}</p>}
      </section>

      <section className="panel">
        <h3>Treatment register</h3>
        {treatments === null ? <p className="muted">Loading…</p>
          : treatments.length === 0 ? <p className="muted">No risks under treatment yet — pick one below to start.</p>
          : (
            <table className="pd-table">
              <thead><tr><th>Risk</th><th>Owner</th><th>Strategy</th><th>Status</th><th>Inherent → residual</th><th>Target</th><th></th></tr></thead>
              <tbody>
                {treatments.map((t) => (
                  <tr key={t.id}>
                    <td><strong>{t.title}</strong><div className="muted small">{t.domain}</div></td>
                    <td>{t.owner || <span className="muted small">unassigned</span>}</td>
                    <td>{t.strategy}</td>
                    <td><span className={`state state-${t.status === 'Closed' || t.status === 'Mitigated' || t.status === 'Accepted' ? 'active' : 'deactivated'}`}>{t.status}</span></td>
                    <td>
                      <span className="mono small">{t.inherentSeverity}×{t.inherentLikelihood}={t.inherentSeverity * t.inherentLikelihood}</span>
                      {t.residualSeverity != null && t.residualLikelihood != null
                        ? <span className="mono small"> → <strong style={{ color: '#6ee7b7' }}>{t.residualSeverity}×{t.residualLikelihood}={t.residualSeverity * t.residualLikelihood}</strong></span>
                        : <span className="muted small"> → not assessed</span>}
                    </td>
                    <td className="muted small">{t.targetDate ? new Date(t.targetDate).toLocaleDateString() : '—'}</td>
                    <td><button className="secondary small" onClick={() => setEditing({ ...t })}>Manage</button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
      </section>

      {editing && (
        <section className="panel">
          <h3>Manage: {editing.title} <span className="muted small">· {editing.domain}</span></h3>
          <div className="enroll-grid">
            <label>Owner<input value={editing.owner ?? ''} onChange={(e) => setEditing({ ...editing, owner: e.target.value })} placeholder="who owns this risk" /></label>
            <label>Strategy<select value={editing.strategy} onChange={(e) => setEditing({ ...editing, strategy: e.target.value })}>{STRATEGIES.map((s) => <option key={s}>{s}</option>)}</select></label>
            <label>Status<select value={editing.status} onChange={(e) => setEditing({ ...editing, status: e.target.value })}>{STATUSES.map((s) => <option key={s}>{s}</option>)}</select></label>
            <label>Target date<input type="date" value={editing.targetDate ? editing.targetDate.slice(0, 10) : ''} onChange={(e) => setEditing({ ...editing, targetDate: e.target.value })} /></label>
            <label>Residual severity<select value={editing.residualSeverity ?? ''} onChange={(e) => setEditing({ ...editing, residualSeverity: e.target.value ? Number(e.target.value) : null })}><option value="">—</option>{[1, 2, 3, 4, 5].map((n) => <option key={n}>{n}</option>)}</select></label>
            <label>Residual likelihood<select value={editing.residualLikelihood ?? ''} onChange={(e) => setEditing({ ...editing, residualLikelihood: e.target.value ? Number(e.target.value) : null })}><option value="">—</option>{[1, 2, 3, 4, 5].map((n) => <option key={n}>{n}</option>)}</select></label>
            <label>Linked remediation action<input value={editing.linkedActionRef ?? ''} onChange={(e) => setEditing({ ...editing, linkedActionRef: e.target.value })} placeholder="e.g. AutonomousActionRequest id" /></label>
            <label className="enroll-wide">Mitigation plan<input value={editing.mitigationPlan ?? ''} onChange={(e) => setEditing({ ...editing, mitigationPlan: e.target.value })} placeholder="what's being done" /></label>
          </div>
          <div className="enroll-actions">
            <button onClick={save} disabled={busy === editing.id}>{busy === editing.id ? 'Saving…' : 'Save'}</button>
            <button className="secondary" onClick={remediate} disabled={busy === editing.id} title="Submit a governed autonomous remediation action for this risk">🛠️ Create remediation action</button>
            <button className="secondary" onClick={() => setEditing(null)}>Cancel</button>
          </div>
          {editing.linkedActionRef && <p className="muted small">Linked remediation action: <span className="mono">{editing.linkedActionRef.slice(0, 8)}</span> · status {editing.status}</p>}
        </section>
      )}

      <section className="panel">
        <h3>Risks needing treatment <span className="muted small">· {untreated.length}</span></h3>
        {untreated.length === 0 ? <p className="muted">Every identified risk is under treatment. 🎉</p>
          : (
            <table className="pd-table">
              <thead><tr><th>Risk</th><th>Domain</th><th>Inherent</th><th></th></tr></thead>
              <tbody>
                {untreated.slice(0, 100).map((r) => (
                  <tr key={r.riskKey}>
                    <td>{r.title}</td>
                    <td className="muted small">{r.domain}</td>
                    <td><span className={`mono small ${r.severity >= 4 && r.likelihood >= 4 ? 'error-text' : ''}`}>{r.severity}×{r.likelihood}={r.severity * r.likelihood}</span></td>
                    <td><button className="small" onClick={() => manage(r)} disabled={busy === r.riskKey}>{busy === r.riskKey ? '…' : 'Manage'}</button></td>
                  </tr>
                ))}
                {untreated.length > 100 && <tr><td colSpan={4} className="muted small">Showing the 100 highest — treat these first.</td></tr>}
              </tbody>
            </table>
          )}
      </section>
    </div>
  )
}
