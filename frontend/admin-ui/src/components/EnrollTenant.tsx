import { useEffect, useMemo, useState } from 'react'
import { api, ApiError } from '../api'
import { useToast } from './Toasts'

// Self-service tenant enrollment (operator console). Phase 1 of the onboarding
// engine: profile → starter pack → branding → admin key, provisioned through
// the SAME audited endpoints an operator uses by hand (create tenant, apply
// template, set config, issue key). No AI yet — the recommender (Phase 2)
// will later pre-fill the pack/posture/branding, but writes through this
// exact flow. Everything here is advisory-until-provisioned: nothing is
// created until the operator clicks Provision.

interface TemplateSummary {
  templateId: string
  name: string
  industry?: string | null
  description?: string | null
  packageCount: number
  benchmarkCount: number
  renewalCount: number
}

interface RecommendedSkill { packageId: string; displayName: string; tier: string; reason: string; confidence: string }
interface RecommendationPack {
  detectedIndustry: string; suggestedTier: string; handlesPhi: boolean
  suggestedSegment?: string | null; suggestedAccent?: string | null
  skills: RecommendedSkill[]; notes: string[]; rationale: string
}

// Regulatory posture presets → backend sensitivity tier + PHI flag. Kept small
// and explicit; the operator picks the client's context, not a raw enum.
const POSTURES = [
  { id: 'standard', label: 'Standard', hint: 'No special regulatory context.', tier: 'Standard', phi: false },
  { id: 'elevated', label: 'Elevated', hint: 'Sensitive business data; tighter audit visibility.', tier: 'Elevated', phi: false },
  { id: 'phi', label: 'Healthcare / PHI (HIPAA)', hint: 'Handles PHI — masking mandatory, access-key required.', tier: 'Regulated', phi: true },
  { id: 'securities', label: 'Securities / Regulated', hint: 'Securities-regulated; access-key required.', tier: 'Regulated', phi: false },
] as const

const ROLES = ['ExecutiveOwner', 'PlatformAdministrator'] as const

type StepStatus = 'pending' | 'running' | 'ok' | 'skip' | 'fail'
interface Step { label: string; status: StepStatus; detail?: string }

function slugify(s: string): string {
  return s.toLowerCase().trim().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 63)
}

export function EnrollTenant({ onProvisioned }: { onProvisioned: (tenantId: string) => void }) {
  const toast = useToast()
  const [templates, setTemplates] = useState<TemplateSummary[]>([])

  // Profile
  const [name, setName] = useState('')
  const [tenantId, setTenantId] = useState('')
  const [tenantIdTouched, setTenantIdTouched] = useState(false)
  const [industry, setIndustry] = useState('')
  const [contactName, setContactName] = useState('')
  const [contactEmail, setContactEmail] = useState('')
  const [notes, setNotes] = useState('')
  const [posture, setPosture] = useState<(typeof POSTURES)[number]['id']>('standard')
  const [templateId, setTemplateId] = useState('') // '' = blank tenant
  const [brand, setBrand] = useState('')
  const [segment, setSegment] = useState('')
  const [accent, setAccent] = useState('#5aa9e6')
  const [adminId, setAdminId] = useState('')
  const [adminName, setAdminName] = useState('')
  const [adminRole, setAdminRole] = useState<(typeof ROLES)[number]>('ExecutiveOwner')

  // AI recommendation
  const [rec, setRec] = useState<RecommendationPack | null>(null)
  const [recBusy, setRecBusy] = useState(false)
  const [recError, setRecError] = useState<string | null>(null)
  const [chosenSkills, setChosenSkills] = useState<Set<string>>(new Set())

  // Provisioning
  const [steps, setSteps] = useState<Step[] | null>(null)
  const [busy, setBusy] = useState(false)
  const [issuedKey, setIssuedKey] = useState<string | null>(null)
  const [doneTenant, setDoneTenant] = useState<string | null>(null)

  useEffect(() => {
    api.get<TemplateSummary[]>('/api/platform/templates').then(setTemplates).catch(() => {})
  }, [])

  // Slug tracks the name until the operator edits it directly.
  useEffect(() => {
    if (!tenantIdTouched) setTenantId(slugify(name))
  }, [name, tenantIdTouched])

  const selectedPosture = POSTURES.find((p) => p.id === posture)!
  const slugValid = /^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$/.test(tenantId)
  const emailish = (s: string) => s.includes('@') && s.length >= 3

  const canProvision = useMemo(() =>
    name.trim().length > 0 && slugValid && tenantId !== 'platform'
    && emailish(adminId) && !busy && doneTenant === null,
    [name, slugValid, tenantId, adminId, busy, doneTenant])

  function set(i: number, status: StepStatus, detail?: string) {
    setSteps((prev) => prev ? prev.map((s, idx) => idx === i ? { ...s, status, detail } : s) : prev)
  }

  async function recommend() {
    if (!name.trim()) return
    setRecBusy(true); setRecError(null)
    try {
      const r = await api.post<RecommendationPack>('/api/platform/onboarding/recommend', {
        name: name.trim(), industry: industry.trim() || null,
        description: notes.trim() || null, regulatoryContext: notes.trim() || null,
      })
      setRec(r)
      setChosenSkills(new Set(r.skills.map((s) => s.packageId)))
    } catch (e) {
      setRecError(e instanceof ApiError ? e.message : String(e))
    } finally { setRecBusy(false) }
  }

  function applySuggestions() {
    if (!rec) return
    setPosture(rec.handlesPhi ? 'phi' : rec.suggestedTier === 'Regulated' ? 'securities' : rec.suggestedTier === 'Elevated' ? 'elevated' : 'standard')
    if (rec.suggestedSegment && !segment) setSegment(rec.suggestedSegment)
    if (rec.suggestedAccent) setAccent(rec.suggestedAccent)
  }

  async function provision() {
    const tid = tenantId
    const useTemplate = templateId !== ''
    const skillsToInstall = rec ? rec.skills.filter((s) => chosenSkills.has(s.packageId)) : []
    const initial: Step[] = [
      { label: `Create tenant "${name.trim()}" (${tid})`, status: 'pending' },
      { label: useTemplate ? `Apply starter pack "${templateId}"` : 'Starter pack (none selected)', status: useTemplate ? 'pending' : 'skip' },
      { label: 'Apply branding & posture', status: 'pending' },
      { label: skillsToInstall.length ? `Install ${skillsToInstall.length} recommended skill(s)` : 'Recommended skills (none selected)', status: skillsToInstall.length ? 'pending' : 'skip' },
      { label: `Issue admin key for ${adminId}`, status: 'pending' },
    ]
    setSteps(initial)
    setBusy(true)
    setIssuedKey(null)

    // 1. Create the tenant. A hard failure here aborts the rest.
    set(0, 'running')
    try {
      await api.post('/api/platform/tenants/', {
        tenantId: tid, name: name.trim(), industry: industry.trim() || null,
        contactName: contactName.trim() || null, contactEmail: contactEmail.trim() || null,
        notes: notes.trim() || null,
      })
      set(0, 'ok')
    } catch (e) {
      set(0, 'fail', e instanceof ApiError ? e.message : String(e))
      setBusy(false)
      return
    }

    // 2. Starter pack (optional). A failure is reported but does not abort —
    // the tenant exists and the operator can retry packages from Packages.
    if (useTemplate) {
      set(1, 'running')
      try {
        const r = await api.post<{ packagesActivated: string[]; packagesSkipped: string[]; errors: string[] }>(
          `/api/platform/templates/${templateId}/apply`, { tenantId: tid })
        set(1, r.errors.length ? 'fail' : 'ok',
          `${r.packagesActivated.length} activated, ${r.packagesSkipped.length} skipped`
          + (r.errors.length ? ` · ${r.errors.length} error(s): ${r.errors[0]}` : ''))
      } catch (e) {
        set(1, 'fail', e instanceof ApiError ? e.message : String(e))
      }
    }

    // 3. Branding & posture.
    set(2, 'running')
    try {
      await api.put(`/api/platform/tenants/${tid}/config`, {
        brand: brand.trim() || name.trim(),
        segment: segment.trim() || industry.trim() || null,
        accentColor: accent,
        handlesPhi: selectedPosture.phi,
        sensitivityTier: selectedPosture.tier,
      })
      set(2, 'ok', `${selectedPosture.label}${selectedPosture.phi ? ' · PHI masking on' : ''}`)
    } catch (e) {
      set(2, 'fail', e instanceof ApiError ? e.message : String(e))
    }

    // 4. Recommended skills (from the AI advisor). Installed via the skill
    // activate endpoint (catalog install with dependencies).
    if (skillsToInstall.length) {
      set(3, 'running')
      let ok = 0; const errs: string[] = []
      for (const s of skillsToInstall) {
        try { await api.post(`/api/platform/tenants/${tid}/skills/${s.packageId}/activate`); ok++ }
        catch (e) { errs.push(`${s.displayName}: ${e instanceof ApiError ? e.message : String(e)}`) }
      }
      set(3, errs.length ? 'fail' : 'ok', `${ok} activated${errs.length ? ` · ${errs.length} error(s): ${errs[0]}` : ''}`)
    }

    // 5. Admin key — shown once.
    set(4, 'running')
    try {
      const r = await api.post<{ accessKey: string }>(`/api/platform/tenants/${tid}/users`, {
        identifier: adminId.trim(), displayName: adminName.trim() || adminId.trim(),
        role: adminRole, email: adminId.trim(),
      })
      setIssuedKey(r.accessKey)
      set(4, 'ok')
    } catch (e) {
      set(4, 'fail', e instanceof ApiError ? e.message : String(e))
    }

    setBusy(false)
    setDoneTenant(tid)
    toast.success(`Tenant '${tid}' provisioned.`)
  }

  function reset() {
    setSteps(null); setIssuedKey(null); setDoneTenant(null)
    setName(''); setTenantId(''); setTenantIdTouched(false); setIndustry('')
    setContactName(''); setContactEmail(''); setNotes(''); setPosture('standard')
    setTemplateId(''); setBrand(''); setSegment(''); setAccent('#5aa9e6')
    setAdminId(''); setAdminName(''); setAdminRole('ExecutiveOwner')
    setRec(null); setChosenSkills(new Set()); setRecError(null)
  }

  // --- Result view ---
  if (steps) {
    return (
      <section className="panel">
        <h2>Enrolling {name.trim()}</h2>
        <ol className="enroll-steps">
          {steps.map((s, i) => (
            <li key={i} className={`enroll-step step-${s.status}`}>
              <span className="step-icon">{s.status === 'ok' ? '✓' : s.status === 'fail' ? '✕' : s.status === 'skip' ? '–' : s.status === 'running' ? '…' : '○'}</span>
              <div>
                <div>{s.label}</div>
                {s.detail && <div className="muted small">{s.detail}</div>}
              </div>
            </li>
          ))}
        </ol>

        {issuedKey && (
          <div className="enroll-key">
            <p><strong>Admin access key — shown once.</strong> Give this to {adminId}; it is stored only as a hash and cannot be retrieved again.</p>
            <code className="mono">{issuedKey}</code>
          </div>
        )}

        {doneTenant && !busy && (
          <div className="enroll-actions">
            <button onClick={() => onProvisioned(doneTenant)}>Open {doneTenant} →</button>
            <button className="secondary" onClick={reset}>Enroll another tenant</button>
          </div>
        )}
      </section>
    )
  }

  // --- Wizard form ---
  return (
    <div>
      <section className="panel">
        <h2>Enroll a new tenant</h2>
        <p className="muted">
          Provision a new client in one flow — profile, starter pack, branding, and an admin
          access key. Nothing is created until you click Provision; each step is audited.
        </p>
      </section>

      <section className="panel">
        <h3>1 · Profile</h3>
        <div className="enroll-grid">
          <label>Company name
            <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Acme Capital" />
          </label>
          <label>Tenant id (slug)
            <input value={tenantId} onChange={(e) => { setTenantId(slugify(e.target.value)); setTenantIdTouched(true) }} placeholder="acme-capital" />
            {tenantId && !slugValid && <span className="error small">3–64 chars, lowercase letters/digits/hyphens.</span>}
            {tenantId === 'platform' && <span className="error small">Reserved id.</span>}
          </label>
          <label>Industry
            <input value={industry} onChange={(e) => setIndustry(e.target.value)} placeholder="Investment management" />
          </label>
          <label>Primary contact
            <input value={contactName} onChange={(e) => setContactName(e.target.value)} placeholder="Jordan Lee" />
          </label>
          <label>Contact email
            <input value={contactEmail} onChange={(e) => setContactEmail(e.target.value)} placeholder="jordan@acme.com" />
          </label>
          <label className="enroll-wide">Notes
            <input value={notes} onChange={(e) => setNotes(e.target.value)} placeholder="Anything the team should know" />
          </label>
        </div>
      </section>

      <section className="panel">
        <h3>AI recommendation</h3>
        <p className="muted small">
          From the profile, recommend the skills, posture, and branding to onboard with. Advisory —
          review and adjust; nothing is applied until you click below or provision.
        </p>
        <button className="secondary" onClick={recommend} disabled={!name.trim() || recBusy}>
          {recBusy ? 'Analyzing…' : rec ? 'Re-run recommendation' : 'Recommend skills & setup'}
        </button>
        {recError && <p className="error small">{recError}</p>}
        {rec && (
          <div className="rec-result">
            <p className="small"><strong>{rec.detectedIndustry}</strong> · suggests <strong>{rec.suggestedTier}</strong> posture{rec.handlesPhi ? ' · PHI' : ''}. {rec.rationale}</p>
            {rec.notes.map((n, i) => <p key={i} className="muted small">• {n}</p>)}
            <div className="rec-skills">
              {rec.skills.map((s) => (
                <label key={s.packageId} className={`rec-skill ${chosenSkills.has(s.packageId) ? 'on' : ''}`}>
                  <input type="checkbox" checked={chosenSkills.has(s.packageId)} onChange={(e) => {
                    const next = new Set(chosenSkills)
                    e.target.checked ? next.add(s.packageId) : next.delete(s.packageId)
                    setChosenSkills(next)
                  }} />
                  <span><strong>{s.displayName}</strong> <span className={`pkg-badge pkg-${s.tier === 'AddOn' ? 'alacarte' : 'essential'}`}>{s.tier}</span></span>
                  <span className="muted small">{s.reason}</span>
                </label>
              ))}
            </div>
            <button className="secondary small" onClick={applySuggestions}>Apply posture &amp; branding to the form</button>
            <span className="muted small" style={{ marginLeft: '0.5rem' }}>{chosenSkills.size} skill(s) will be installed on provision.</span>
          </div>
        )}
      </section>

      <section className="panel">
        <h3>2 · Regulatory posture</h3>
        <div className="enroll-postures">
          {POSTURES.map((p) => (
            <button key={p.id} className={`posture-opt ${posture === p.id ? 'on' : ''}`} onClick={() => setPosture(p.id)}>
              <strong>{p.label}</strong>
              <span className="muted small">{p.hint}</span>
            </button>
          ))}
        </div>
      </section>

      <section className="panel">
        <h3>3 · Starter pack</h3>
        <p className="muted small">A captured industry template loads and activates its packages, benchmarks, and renewals. Optional — you can add packages later.</p>
        <select value={templateId} onChange={(e) => setTemplateId(e.target.value)} style={{ minWidth: '20rem' }}>
          <option value="">Blank tenant — no starter pack</option>
          {templates.map((t) => (
            <option key={t.templateId} value={t.templateId}>
              {t.name}{t.industry ? ` · ${t.industry}` : ''} ({t.packageCount} pkg, {t.benchmarkCount} bench, {t.renewalCount} renew)
            </option>
          ))}
        </select>
        {templates.length === 0 && <p className="muted small">No templates captured yet — capture one from a well-configured tenant in Clients, or leave blank.</p>}
      </section>

      <section className="panel">
        <h3>4 · Branding</h3>
        <div className="enroll-grid">
          <label>Brand (shell name)
            <input value={brand} onChange={(e) => setBrand(e.target.value)} placeholder={name || 'Defaults to company name'} />
          </label>
          <label>Segment label
            <input value={segment} onChange={(e) => setSegment(e.target.value)} placeholder={industry || 'e.g. Multi-Manager Trading'} />
          </label>
          <label>Accent colour
            <span className="enroll-accent">
              <input type="color" value={accent} onChange={(e) => setAccent(e.target.value)} />
              <input value={accent} onChange={(e) => setAccent(e.target.value)} style={{ width: '7rem' }} />
            </span>
          </label>
        </div>
      </section>

      <section className="panel">
        <h3>5 · Admin contact</h3>
        <div className="enroll-grid">
          <label>Admin identifier (email)
            <input value={adminId} onChange={(e) => setAdminId(e.target.value)} placeholder="admin@acme.com" />
            {adminId && !emailish(adminId) && <span className="error small">Use the admin's email.</span>}
          </label>
          <label>Display name
            <input value={adminName} onChange={(e) => setAdminName(e.target.value)} placeholder="Jordan Lee" />
          </label>
          <label>Role
            <select value={adminRole} onChange={(e) => setAdminRole(e.target.value as (typeof ROLES)[number])}>
              {ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
            </select>
          </label>
        </div>
        <p className="muted small">The admin's access key is generated and shown once at the end.</p>
      </section>

      <section className="panel">
        <button onClick={provision} disabled={!canProvision}>Provision tenant</button>
        {!canProvision && !busy && <span className="muted small" style={{ marginLeft: '0.75rem' }}>Company name, a valid tenant id, and an admin email are required.</span>}
      </section>
    </div>
  )
}
