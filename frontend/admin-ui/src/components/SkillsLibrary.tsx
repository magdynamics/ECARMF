import { useEffect, useMemo, useState } from 'react'
import { api, ApiError } from '../api'
import { useToast } from './Toasts'

// Platform Skills Library (admin). The catalogue of every skill on the
// platform, showing its packaging (Essential = bundled, or À la carte =
// metered) and — the point of this screen — its VALUE: the controls it
// provides and the assertions those controls protect. The admin can move a
// skill between Essential and À la carte and set its price.

interface ControlCoverage { controlId: string; name: string; assertion: string; outcome: string }
interface SkillValue {
  packageId: string
  displayName: string
  tier: string
  packaging: 'Essential' | 'AlaCarte'
  monthlyPrice: number
  currency: string
  whatItDoes?: string | null
  executableControls: number
  referenceCatalogs: number
  assertionsCovered: string[]
  controls: ControlCoverage[]
  installedInTenants: string[]
}

export function SkillsLibrary() {
  const toast = useToast()
  const [skills, setSkills] = useState<SkillValue[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [filter, setFilter] = useState('')
  const [pkgFilter, setPkgFilter] = useState<'all' | 'Essential' | 'AlaCarte'>('all')
  const [open, setOpen] = useState<string | null>(null)
  const [editing, setEditing] = useState<string | null>(null)
  const [draftPackaging, setDraftPackaging] = useState<'Essential' | 'AlaCarte'>('Essential')
  const [draftPrice, setDraftPrice] = useState('0')
  const [saving, setSaving] = useState(false)
  const [note, setNote] = useState<string | null>(null)

  async function load() {
    setError(null)
    try {
      setSkills(await api.get<SkillValue[]>('/api/platform/skills/library'))
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e)); setSkills([])
    }
  }
  useEffect(() => { void load() }, [])

  function startEdit(s: SkillValue) {
    setEditing(s.packageId); setDraftPackaging(s.packaging); setDraftPrice(String(s.monthlyPrice)); setNote(null)
  }

  async function save(s: SkillValue) {
    setSaving(true); setNote(null); setError(null)
    try {
      const r = await api.put<{ message: string }>(`/api/platform/skills/${s.packageId}/packaging`, {
        packaging: draftPackaging, monthlyPrice: Number(draftPrice) || 0,
      })
      setNote(null)
      toast.success(`${s.displayName}: ${r.message}`)
      setEditing(null)
      await load()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e))
    } finally { setSaving(false) }
  }

  const f = filter.trim().toLowerCase()
  const list = useMemo(() => (skills ?? []).filter((s) =>
    (pkgFilter === 'all' || s.packaging === pkgFilter)
    && (!f || s.displayName.toLowerCase().includes(f) || s.packageId.toLowerCase().includes(f)
      || s.assertionsCovered.some((a) => a.toLowerCase().includes(f))
      || s.controls.some((c) => c.controlId.toLowerCase().includes(f) || c.name.toLowerCase().includes(f)))),
    [skills, f, pkgFilter])

  const alaCarte = (skills ?? []).filter((s) => s.packaging === 'AlaCarte')
  const mrr = alaCarte.reduce((t, s) => t + s.monthlyPrice, 0)
  const totalControls = (skills ?? []).reduce((t, s) => t + s.executableControls, 0)

  return (
    <div>
      <section className="panel">
        <h2>Skills Library</h2>
        <p className="muted">
          Every skill on the platform, its packaging, and its value — the controls it provides and the
          assertions those controls protect. Bundle a skill into the core/industry package (Essential) or
          sell it à la carte. À la carte skills bill per tenant when active.
        </p>
        <div style={{ display: 'flex', gap: '0.6rem', alignItems: 'center', flexWrap: 'wrap' }}>
          <input placeholder="Search skills, controls, assertions…" value={filter} onChange={(e) => setFilter(e.target.value)} style={{ minWidth: '18rem' }} />
          <select value={pkgFilter} onChange={(e) => setPkgFilter(e.target.value as typeof pkgFilter)}>
            <option value="all">All packaging</option>
            <option value="Essential">Essential (bundled)</option>
            <option value="AlaCarte">À la carte</option>
          </select>
          {skills && (
            <span className="muted small">
              {skills.length} skills · {totalControls} controls · {alaCarte.length} à la carte ·
              <strong> {alaCarte[0]?.currency ?? 'USD'} {mrr.toLocaleString()}</strong>/mo list price
            </span>
          )}
        </div>
        {error && <p className="error small">{error}</p>}
        {note && <p className="muted small">{note}</p>}
      </section>

      <section className="panel">
        {skills === null ? <p className="muted">Loading library…</p>
          : list.length === 0 ? <p className="muted">No skills match.</p>
          : (
            <div className="cap-list">
              {list.map((s) => (
                <div key={s.packageId} className="skl-row">
                  <div
                    className="skl-head"
                    role="button"
                    tabIndex={0}
                    aria-expanded={open === s.packageId}
                    onClick={() => setOpen(open === s.packageId ? null : s.packageId)}
                    onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setOpen(open === s.packageId ? null : s.packageId) } }}
                  >
                    <span className="skl-caret">{open === s.packageId ? '▾' : '▸'}</span>
                    <div className="cap-main">
                      <div>
                        <strong>{s.displayName}</strong>{' '}
                        <span className={`pkg-badge pkg-${s.packaging.toLowerCase()}`}>
                          {s.packaging === 'AlaCarte' ? 'À la carte' : 'Essential'}
                        </span>{' '}
                        <span className="muted small">{s.tier}</span>
                      </div>
                      <div className="muted small">
                        {s.executableControls} control{s.executableControls === 1 ? '' : 's'}
                        {s.referenceCatalogs > 0 && ` · ${s.referenceCatalogs} reference catalog${s.referenceCatalogs === 1 ? '' : 's'}`}
                        {s.assertionsCovered.length > 0 && ` · protects ${s.assertionsCovered.length} assertion${s.assertionsCovered.length === 1 ? '' : 's'}`}
                        {s.installedInTenants.length > 0 && ` · in ${s.installedInTenants.length} tenant(s)`}
                      </div>
                    </div>
                    <span className="skl-price">{s.monthlyPrice > 0 ? `${s.currency} ${s.monthlyPrice.toLocaleString()}/mo` : 'Included'}</span>
                  </div>

                  {open === s.packageId && (
                    <div className="skl-detail">
                      {s.whatItDoes && <p className="muted small">{s.whatItDoes}</p>}

                      {s.assertionsCovered.length > 0 && (
                        <div className="skl-assertions">
                          {s.assertionsCovered.map((a) => <span key={a} className="assert-chip">{a}</span>)}
                        </div>
                      )}

                      {s.controls.length > 0 ? (
                        <table className="skl-table">
                          <thead><tr><th>Control</th><th>Protects (assertion)</th><th>Outcome</th></tr></thead>
                          <tbody>
                            {s.controls.map((c) => (
                              <tr key={c.controlId}>
                                <td><span className="mono small">{c.controlId}</span> {c.name}</td>
                                <td>{c.assertion}</td>
                                <td><span className={`state state-${c.outcome.toLowerCase()}`}>{c.outcome}</span></td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      ) : (
                        <p className="muted small">No executable controls; value is carried in {s.referenceCatalogs} reference catalog(s).</p>
                      )}

                      <div className="skl-edit">
                        {editing === s.packageId ? (
                          <>
                            <select value={draftPackaging} onChange={(e) => setDraftPackaging(e.target.value as 'Essential' | 'AlaCarte')}>
                              <option value="Essential">Essential (bundled)</option>
                              <option value="AlaCarte">À la carte</option>
                            </select>
                            {draftPackaging === 'AlaCarte' && (
                              <label className="skl-price-in">Price/mo
                                <input type="number" min="0" value={draftPrice} onChange={(e) => setDraftPrice(e.target.value)} style={{ width: '7rem' }} />
                              </label>
                            )}
                            <button onClick={() => save(s)} disabled={saving}>{saving ? 'Saving…' : 'Save'}</button>
                            <button className="secondary" onClick={() => setEditing(null)}>Cancel</button>
                          </>
                        ) : (
                          <button className="secondary small" onClick={() => startEdit(s)}>Change packaging</button>
                        )}
                      </div>
                    </div>
                  )}
                </div>
              ))}
            </div>
          )}
      </section>
    </div>
  )
}
