import { useCallback, useEffect, useMemo, useState } from 'react'
import { api, ApiError } from '../api'
import { useToast } from './Toasts'

// Skills on a tenant's profile (operator console). A skill is a knowledge
// package presented commercially — Core (included), Industry (bundle), or
// Add-on (metered). The operator turns skills on/off for a client; activating
// installs from the library with dependencies, and priced skills roll into the
// tenant's bill. Mirrors the Package Library's target-tenant pattern.

interface SkillView {
  packageId: string
  version: string
  displayName: string
  tier: 'Core' | 'Industry' | 'AddOn'
  monthlyPrice: number
  currency: string
  whatItDoes?: string | null
  controls: number
  kpis: number
  agents: number
  dependencies: string[]
  installed: boolean
  active: boolean
}

const TIERS: { id: SkillView['tier']; label: string; blurb: string }[] = [
  { id: 'Core', label: 'Core', blurb: 'Included in the base fee — integrations, renewals, statements, foundations.' },
  { id: 'Industry', label: 'Industry', blurb: 'Industry-specific skills, priced per skill.' },
  { id: 'AddOn', label: 'Add-ons', blurb: 'Premium metered capabilities like autonomous orchestration & financial continuity.' },
]

export function Skills({ tenant }: { tenant: string; user: string }) {
  const [tenants, setTenants] = useState<string[]>([])
  const [target, setTarget] = useState('')
  const [skills, setSkills] = useState<SkillView[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const toast = useToast()

  useEffect(() => {
    api.get<{ tenantId: string }[]>('/api/platform/tenants')
      .then((list) => setTenants(list.map((t) => t.tenantId).filter((t) => t.toLowerCase() !== 'platform')))
      .catch(() => {})
  }, [])

  useEffect(() => {
    if (!target && tenant && tenant.toLowerCase() !== 'platform') setTarget(tenant)
  }, [tenant, target])

  const load = useCallback(async (t: string) => {
    if (!t) { setSkills(null); return }
    setError(null)
    try {
      setSkills(await api.get<SkillView[]>(`/api/platform/tenants/${t}/skills`))
    } catch (e) {
      setError(e instanceof ApiError ? e.message : String(e)); setSkills([])
    }
  }, [])

  useEffect(() => { void load(target) }, [target, load])

  async function toggle(s: SkillView) {
    if (!target) return
    setBusy(s.packageId); setError(null)
    const action = s.active ? 'deactivate' : 'activate'
    try {
      const r = await api.post<{ message: string }>(`/api/platform/tenants/${target}/skills/${s.packageId}/${action}`)
      toast.success(`${s.displayName}: ${r.message}`)
      await load(target)
    } catch (e) {
      const msg = e instanceof ApiError ? e.message : String(e)
      setError(msg); toast.error(msg)
    } finally {
      setBusy(null)
    }
  }

  const monthly = useMemo(() =>
    (skills ?? []).filter((s) => s.active && s.monthlyPrice > 0).reduce((sum, s) => sum + s.monthlyPrice, 0),
    [skills])
  const activeCount = (skills ?? []).filter((s) => s.active).length
  const currency = skills?.[0]?.currency ?? 'USD'

  return (
    <div>
      <section className="panel">
        <h2>Skills</h2>
        <p className="muted">
          Turn capabilities on or off for a client. A skill is a knowledge package; activating installs
          it (with dependencies). Core skills are included; Industry and Add-on skills are billed monthly.
        </p>
        <div style={{ display: 'flex', gap: '0.8rem', alignItems: 'center', flexWrap: 'wrap' }}>
          <label>Tenant
            <select value={target} onChange={(e) => setTarget(e.target.value)}>
              <option value="">Choose a tenant…</option>
              {tenants.map((t) => <option key={t} value={t}>{t}</option>)}
            </select>
          </label>
          {skills && (
            <span className="muted small">
              {activeCount} active · <strong>{currency} {monthly.toLocaleString()}</strong>/mo in priced skills
            </span>
          )}
        </div>
        {error && <p className="error small">{error}</p>}
      </section>

      {!target ? (
        <section className="panel"><p className="muted">Pick a tenant to manage its skills.</p></section>
      ) : skills === null ? (
        <section className="panel"><p className="muted">Loading skills…</p></section>
      ) : (
        TIERS.map((tier) => {
          const rows = skills.filter((s) => s.tier === tier.id)
          if (rows.length === 0) return null
          return (
            <section key={tier.id} className="panel">
              <h3>{tier.label} <span className="muted small">· {rows.filter((r) => r.active).length}/{rows.length} on</span></h3>
              <p className="muted small">{tier.blurb}</p>
              <div className="skill-grid">
                {rows.map((s) => (
                  <div key={s.packageId} className={`skill-card ${s.active ? 'on' : ''}`}>
                    <div className="skill-head">
                      <strong>{s.displayName}</strong>
                      <span className="skill-price">
                        {s.monthlyPrice > 0 ? `${s.currency} ${s.monthlyPrice.toLocaleString()}/mo` : 'Included'}
                      </span>
                    </div>
                    {s.whatItDoes && <p className="muted small skill-desc">{s.whatItDoes}</p>}
                    <div className="muted small">{s.controls}C · {s.kpis}K · {s.agents}A{s.dependencies.length ? ` · needs ${s.dependencies.length} dep` : ''}</div>
                    <button
                      className={s.active ? 'secondary' : ''}
                      onClick={() => toggle(s)}
                      disabled={busy === s.packageId}
                    >
                      {busy === s.packageId ? '…' : s.active ? 'Deactivate' : s.installed ? 'Activate' : 'Add & activate'}
                    </button>
                  </div>
                ))}
              </div>
            </section>
          )
        })
      )}
    </div>
  )
}
