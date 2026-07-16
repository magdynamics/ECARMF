import { Component, useEffect, useState, type ReactNode } from 'react'
import './App.css'
import { api, getApiKey, getTenant, getUser, setApiKey, setTenant, setUser } from './api'
import { tenantConfig, mergeConfig, type TenantConfigDto } from './tenantConfig'
import { Advisor } from './components/Advisor'
import { AiSettings } from './components/AiSettings'
import { Allocations } from './components/Allocations'
import { Benchmarks } from './components/Benchmarks'
import { Billing } from './components/Billing'
import { Clients } from './components/Clients'
import { Dashboard } from './components/Dashboard'
import { DataEntry } from './components/DataEntry'
import { EmailSettings } from './components/EmailSettings'
import { HealthBoard } from './components/HealthBoard'
import { Home } from './components/Home'
import { OperatorHome } from './components/OperatorHome'
import { Integrations } from './components/Integrations'
import { Library } from './components/Library'
import { Organization } from './components/Organization'
import { PackageInspector } from './components/PackageInspector'
import { RecordActivity } from './components/RecordActivity'
import { RiskRegister } from './components/RiskRegister'
import { ControlsExplorer } from './components/ControlsExplorer'
import { CapabilityExplorer } from './components/CapabilityExplorer'
import { Agents } from './components/Agents'
import { EnrollTenant } from './components/EnrollTenant'
import { SystemMap } from './components/SystemMap'
import { Glossary } from './components/Glossary'
import { Demos } from './components/Demos'
import { CommandPalette } from './components/CommandPalette'
import { Icon } from './components/Icon'
import { Periods } from './components/Periods'
import { Cases } from './components/Cases'
import { PlatformRisk } from './components/PlatformRisk'
import { RiskTreatments } from './components/RiskTreatments'
import { PlatformActions } from './components/PlatformActions'
import { BoardPack } from './components/BoardPack'
import { AuditLog } from './components/AuditLog'
import { PeerBenchmark } from './components/PeerBenchmark'
import { PackageCatalog } from './components/PackageCatalog'
import { Skills } from './components/Skills'
import { SkillsLibrary } from './components/SkillsLibrary'
import { Renewals } from './components/Renewals'
import { Reports } from './components/Reports'
import { StatementReview } from './components/StatementReview'
import { Treasury } from './components/Treasury'

const SEEDED_USERS = [
  { id: 'owner@platform', label: 'Owner / Executive' },
  { id: 'admin@platform', label: 'Platform Administrator' },
]

// A crash in one screen must degrade to an in-place error panel — without
// this, React unmounts the whole tree and the user sees a black page with
// no way back (found live: the Organization detail panel did exactly that).
class ScreenBoundary extends Component<
  { screen: string; children: ReactNode },
  { failed: Error | null }
> {
  state = { failed: null as Error | null }

  static getDerivedStateFromError(error: Error) {
    return { failed: error }
  }

  componentDidUpdate(prev: { screen: string }) {
    // Navigating to another tab clears the failure.
    if (prev.screen !== this.props.screen && this.state.failed) {
      this.setState({ failed: null })
    }
  }

  render() {
    if (this.state.failed) {
      return (
        <section className="panel">
          <h2>This screen hit an error</h2>
          <p className="muted">
            The rest of the app is fine — pick another tab, or reload this one.
            Please report what you clicked, along with this message:
          </p>
          <p className="mono small">{String(this.state.failed)}</p>
          <button onClick={() => this.setState({ failed: null })}>Reload this screen</button>
        </section>
      )
    }
    return this.props.children
  }
}

// Sidebar navigation, grouped by what the user is doing. The Platform group
// is the operator console (clients & billing) — it requires the reserved
// 'platform' tenant, and the gate below offers the switch.
const NAV: { tab: string; label: string; icon: string; group: string }[] = [
  { tab: 'home', label: 'Start Here', icon: 'home', group: '' },
  { tab: 'systemmap', label: 'System Map', icon: 'map', group: '' },
  { tab: 'glossary', label: 'Dictionary', icon: 'book', group: '' },
  { tab: 'explore', label: 'Capability Explorer', icon: 'compass', group: '' },
  { tab: 'organization', label: 'Organization', icon: 'building', group: 'Setup' },
  { tab: 'packages', label: 'Packages', icon: 'package', group: 'Setup' },
  { tab: 'controls', label: 'Controls', icon: 'shield', group: 'Setup' },
  { tab: 'integrations', label: 'Integrations', icon: 'plug', group: 'Setup' },
  { tab: 'benchmarks', label: 'Benchmarks', icon: 'target', group: 'Setup' },
  { tab: 'renewals', label: 'Renewals', icon: 'calendar', group: 'Setup' },
  { tab: 'treasury', label: 'AI Treasury', icon: 'landmark', group: 'Setup' },
  { tab: 'ai', label: 'AI Backend', icon: 'cpu', group: 'Setup' },
  { tab: 'dataentry', label: 'Data Entry', icon: 'inbox-in', group: 'Input' },
  { tab: 'statements', label: 'Statement Review', icon: 'file-search', group: 'Input' },
  { tab: 'activity', label: 'Record Activity', icon: 'list', group: 'Output' },
  { tab: 'dashboard', label: 'Dashboard', icon: 'gauge', group: 'Output' },
  { tab: 'risk', label: 'Risk Register', icon: 'alert-triangle', group: 'Output' },
  { tab: 'risktreatment', label: 'Risk Treatment', icon: 'wrench', group: 'Output' },
  { tab: 'periods', label: 'Period Analysis', icon: 'trend-up', group: 'Output' },
  { tab: 'cases', label: 'Cases', icon: 'folders', group: 'Output' },
  { tab: 'reports', label: 'Reports', icon: 'file-text', group: 'Output' },
  { tab: 'boardpack', label: 'Board Pack', icon: 'book-open', group: 'Output' },
  { tab: 'peer', label: 'Peer Benchmark', icon: 'signal', group: 'Output' },
  { tab: 'audit', label: 'Audit Trail', icon: 'clipboard-check', group: 'Output' },
  { tab: 'library', label: 'Library', icon: 'archive', group: 'Output' },
  { tab: 'allocations', label: 'Capital Flows', icon: 'briefcase', group: 'Output' },
  { tab: 'advisor', label: 'AI Advisor', icon: 'bot', group: 'Output' },
  { tab: 'agents', label: 'AI Agents', icon: 'sparkles', group: 'Output' },
  { tab: 'actions', label: 'Action Center', icon: 'inbox', group: 'Platform' },
  { tab: 'platformrisk', label: 'Platform Risk', icon: 'alert-octagon', group: 'Platform' },
  { tab: 'health', label: 'Health Board', icon: 'activity', group: 'Platform' },
  { tab: 'enroll', label: 'Enroll Tenant', icon: 'user-plus', group: 'Platform' },
  { tab: 'catalog', label: 'Package Library', icon: 'library', group: 'Platform' },
  { tab: 'skills', label: 'Skills', icon: 'puzzle', group: 'Platform' },
  { tab: 'skillslibrary', label: 'Skills Library', icon: 'grid', group: 'Platform' },
  { tab: 'demos', label: 'Demo Twins', icon: 'play', group: 'Platform' },
  { tab: 'clients', label: 'Clients', icon: 'users', group: 'Platform' },
  { tab: 'billing', label: 'Billing', icon: 'credit-card', group: 'Platform' },
  { tab: 'email', label: 'Email', icon: 'mail', group: 'Platform' },
]

interface Me {
  tenantId: string
  tenantName: string
  identifier: string
  displayName: string
  viaApiKey: boolean
  isPlatformOperator: boolean
}

// Bookmarks and desktop shortcuts can pin a tenant: ?tenant=platform
// always lands on the operator console regardless of what a previous
// session left in localStorage. The parameter is adopted once and
// stripped from the address bar.
function tenantFromUrlOrStorage(): string {
  const fromUrl = new URLSearchParams(window.location.search).get('tenant')
  if (fromUrl && fromUrl.trim()) {
    setTenant(fromUrl.trim())
    const url = new URL(window.location.href)
    url.searchParams.delete('tenant')
    window.history.replaceState({}, '', url)
    return fromUrl.trim()
  }
  return getTenant()
}

function App() {
  // In key mode the identity comes from the credential, so resolve "who am I"
  // (/api/me) from the operator home tenant first — starting on a persisted
  // client tenant would make an operator look like a client. Non-operator
  // client keys resolve to their own tenant regardless (act-as is ignored).
  // Persist 'platform' to storage too (not just React state): api.ts reads the
  // act-as tenant from storage, so a stale stored tenant would otherwise be
  // sent on the very first /api/me and mask an operator as a client.
  const [tenant, setTenantState] = useState(() => {
    if (getApiKey()) {
      setTenant('platform')
      return 'platform'
    }
    return tenantFromUrlOrStorage()
  })
  const [tenantInput, setTenantInput] = useState(tenant)
  const [user, setUserState] = useState(getUser())
  const [apiKeyInput, setApiKeyInput] = useState('')
  const [me, setMe] = useState<Me | null>(null)
  const [signedInWithKey, setSignedInWithKey] = useState(!!getApiKey())
  const [tab, setTab] = useState('home')
  const [navOpen, setNavOpen] = useState(false)
  const [knownTenants, setKnownTenants] = useState<string[]>([])
  const [tenantWarning, setTenantWarning] = useState<string | null>(null)
  // When the deployment is key-only (header identity disabled), an
  // unauthenticated visitor must sign in with an access key — the header-mode
  // Tenant/As controls do nothing. Detect the mode so we can show a real
  // sign-in screen instead of a dead console.
  const [keyOnly, setKeyOnly] = useState(false)
  // Server-persisted shell branding for the tenant currently being viewed.
  const [serverCfg, setServerCfg] = useState<TenantConfigDto | null>(null)
  // Global search (⌘K / Ctrl-K).
  const [paletteOpen, setPaletteOpen] = useState(false)
  // Collapsible nav groups, persisted — 40 items is too many to show flat.
  // First run collapses the operator group unless you're on the operator tenant.
  const [collapsedGroups, setCollapsedGroups] = useState<Set<string>>(() => {
    const stored = localStorage.getItem('ecarmf.nav.collapsed')
    if (stored !== null) return new Set(stored.split(',').filter(Boolean))
    return new Set(getTenant().toLowerCase() === 'platform' ? [] : ['Platform'])
  })

  function toggleGroup(group: string) {
    setCollapsedGroups((prev) => {
      const next = new Set(prev)
      next.has(group) ? next.delete(group) : next.add(group)
      localStorage.setItem('ecarmf.nav.collapsed', [...next].join(','))
      return next
    })
  }

  function openTab(next: string) {
    setTab(next)
    setNavOpen(false)
    // Navigating into a collapsed group (via ⌘K, System Map, Home cards…)
    // expands it, so the active item is never hidden from its own sidebar.
    const group = NAV.find((n) => n.tab === next)?.group
    if (group && collapsedGroups.has(group)) toggleGroup(group)
    // A new screen starts at its top — carrying the previous screen's
    // scroll position leaves users staring at the middle of a page.
    window.scrollTo({ top: 0 })
  }

  // Autocomplete + validation list. In header mode, fetched with explicit
  // operator headers regardless of the tenant currently viewed — otherwise a
  // user stranded on a mistyped tenant has no list, no suggestions, and no
  // visible way back. In key mode the operator loads it via their credential
  // (see the /api/me effect below, while still on the platform tenant).
  // Non-operators get a 403 and simply no autocomplete.
  useEffect(() => {
    if (signedInWithKey) return
    fetch('/api/platform/tenants', {
      headers: { 'X-Tenant-Id': 'platform', 'X-User-Id': user || 'admin@platform' },
    })
      .then((r) => (r.ok ? r.json() : Promise.reject(r.status)))
      .then((list: { tenantId: string }[]) =>
        setKnownTenants(['platform', ...list.map((t) => t.tenantId)]))
      .catch(() => {})
  }, [user, signedInWithKey])

  useEffect(() => {
    if (!signedInWithKey) {
      setMe(null)
      return
    }
    api
      .get<Me>('/api/me')
      .then((m) => {
        setMe(m)
        setUserState(m.identifier)
        if (m.isPlatformOperator) {
          // Operator: stay on the platform tenant and load the client list
          // for the switcher (this call runs while still act-as platform).
          api
            .get<{ tenantId: string }[]>('/api/platform/tenants')
            .then((list) => setKnownTenants(['platform', ...list.map((t) => t.tenantId)]))
            .catch(() => {})
        } else {
          // Client key: bound to its own tenant.
          setTenant(m.tenantId)
          setTenantState(m.tenantId)
          setTenantInput(m.tenantId)
        }
      })
      .catch(() => {
        setApiKey('')
        setSignedInWithKey(false)
      })
  }, [signedInWithKey])

  function applyTenant(value?: string) {
    const typed = (value ?? tenantInput).trim()
    if (!typed) return
    let next = typed
    // Guard against ghost tenants ("jj" instead of "jj-fish"): when the
    // real list is known, a unique prefix auto-completes; an unknown name
    // is refused with the closest suggestions instead of silently opening
    // an empty workspace with no way back.
    if (knownTenants.length > 0 && !knownTenants.some((t) => t.toLowerCase() === typed.toLowerCase())) {
      const matches = knownTenants.filter((t) => t.toLowerCase().includes(typed.toLowerCase()))
      if (matches.length === 1) {
        next = matches[0]
      } else {
        setTenantWarning(
          matches.length > 1
            ? `'${typed}' matches several tenants: ${matches.join(', ')} — pick one.`
            : `No tenant named '${typed}' exists. Known tenants: ${knownTenants.join(', ')}.`)
        return
      }
    }
    setTenantWarning(null)
    setTenant(next)
    setTenantState(next)
    setTenantInput(next)
  }

  function switchUser(id: string) {
    setUser(id)
    setUserState(id)
  }

  // Global ⌘K / Ctrl-K opens search from anywhere.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && (e.key === 'k' || e.key === 'K')) {
        e.preventDefault()
        setPaletteOpen((o) => !o)
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  // Learn whether this deployment allows header identity (dev) or is key-only
  // (production lockdown) so an unauthenticated visitor gets a sign-in screen.
  useEffect(() => {
    fetch('/auth-mode')
      .then((r) => (r.ok ? r.json() : null))
      .then((m) => { if (m) setKeyOnly(m.headerIdentityAllowed === false) })
      .catch(() => {})
  }, [])

  function signIn() {
    setApiKey(apiKeyInput.trim())
    setApiKeyInput('')
    // Resolve identity from the operator home tenant first; /api/me then
    // decides operator vs client and routes accordingly.
    setTenant('platform')
    setTenantState('platform')
    setTenantInput('platform')
    setSignedInWithKey(true)
  }

  function signOut() {
    setApiKey('')
    setSignedInWithKey(false)
    setMe(null)
  }

  // A platform operator signed in with a key can view any tenant via the
  // "act as" mechanism, so their effective tenant tracks the SELECTED tenant,
  // not the credential's home. A client key stays bound to its own tenant.
  const operator = signedInWithKey && me?.isPlatformOperator === true
  const effectiveTenant = signedInWithKey ? (operator ? tenant : (me?.tenantId ?? '')) : tenant
  const effectiveUser = signedInWithKey ? (me?.identifier ?? '') : user
  const isPlatformTab = tab === 'clients' || tab === 'billing' || tab === 'email' || tab === 'health' || tab === 'enroll' || tab === 'catalog' || tab === 'skills' || tab === 'skillslibrary' || tab === 'demos' || tab === 'platformrisk' || tab === 'actions'
  const onPlatformTenant = effectiveTenant.toLowerCase() === 'platform'

  // Load the viewed tenant's persisted branding (api.ts sends the act-as
  // tenant header, so an operator sees each client's own look). Cleared while
  // switching so a stale brand never flashes on the next tenant.
  useEffect(() => {
    setServerCfg(null)
    if (!effectiveTenant) return
    let live = true
    api.get<TenantConfigDto>('/api/tenant-config')
      .then((c) => { if (live) setServerCfg(c) })
      .catch(() => {})
    return () => { live = false }
  }, [effectiveTenant])

  // Tenant-Aware Shell (§2.1): per-tenant branding/terminology/posture. The
  // static tenantConfig.ts is the fallback; the server-persisted config
  // (set via the enroll wizard / config endpoint) overlays it, so onboarding
  // a new tenant's look needs no code change.
  const cfg = mergeConfig(tenantConfig(effectiveTenant), serverCfg)

  // Role-aware nav: a client key can never reach operator surfaces, so the
  // Platform group disappears entirely from its sidebar (screens stay gated
  // regardless). Header mode keeps the full nav — the OperatorGate handles it.
  const visibleNav = signedInWithKey && !operator
    ? NAV.filter((n) => n.group !== 'Platform')
    : NAV

  // Operator gate: the Platform group needs the reserved operator tenant.
  const OperatorGate = () => (
    <section className="panel">
      <h2>Platform operator console</h2>
      <p className="muted">
        Clients and Billing are managed from the reserved operator tenant (<code>platform</code>)
        — client tenants can never see or reach each other's data here. You are currently on
        tenant <strong>{effectiveTenant}</strong>.
      </p>
      {signedInWithKey && !operator ? (
        <p className="muted small">
          You are signed in with a client access key, which is bound to its own tenant. Sign out
          and use an operator access key to manage clients.
        </p>
      ) : (
        <button onClick={() => { if (!operator) switchUser('admin@platform'); applyTenant('platform') }}>
          Switch to the operator tenant
        </button>
      )}
    </section>
  )

  // Key-only deployment, not signed in: show a dedicated sign-in screen so the
  // access key is the obvious (and only) way in — the header-mode console would
  // just 401 on every call and look broken.
  if (keyOnly && !signedInWithKey) {
    return (
      <div className="app">
        <header className="topbar">
          <div className="brand">
            <h1>ECARMF <span className="accent">Platform Kernel</span></h1>
          </div>
        </header>
        <div className="signin-screen">
          <section className="panel signin-card">
            <h2>Sign in</h2>
            <p className="muted">
              This platform requires an access key. Paste your operator or client key to continue.
            </p>
            <label className="signin-key">
              Access key
              <input
                type="password"
                placeholder="ecarmf_…"
                autoComplete="off"
                name="ecarmf-access-key"
                value={apiKeyInput}
                onChange={(e) => setApiKeyInput(e.target.value)}
                onKeyDown={(e) => e.key === 'Enter' && apiKeyInput.trim() && signIn()}
                autoFocus
              />
            </label>
            <button onClick={signIn} disabled={!apiKeyInput.trim()}>Sign in</button>
          </section>
        </div>
      </div>
    )
  }

  return (
    <div className="app" style={{ ['--tenant-accent' as string]: cfg.accent }}>
      <header className="topbar">
        <div className="brand">
          <button className="menu-toggle" onClick={() => setNavOpen((o) => !o)} aria-label="Menu"><Icon name="menu" size={18} /></button>
          <h1>
            ECARMF <span className="accent">Platform Kernel</span>
          </h1>
          <button className="search-btn" onClick={() => setPaletteOpen(true)} title="Search (Ctrl-K)">
            <Icon name="search" size={14} /> Search <span className="kbd">Ctrl K</span>
          </button>
        </div>
        <div className="tenant-bar">
          {signedInWithKey ? (
            <>
              <span className="small">
                <strong>{me?.displayName ?? '…'}</strong>{' '}
                <span className="muted">· {operator ? effectiveTenant : (me?.tenantName ?? me?.tenantId)}</span>
              </span>
              {operator && (
                <label>
                  View
                  <select
                    className="tenant-select"
                    value={knownTenants.includes(effectiveTenant) ? effectiveTenant : ''}
                    onChange={(e) => applyTenant(e.target.value)}
                  >
                    {!knownTenants.includes(effectiveTenant) && <option value="">Select a client…</option>}
                    {knownTenants.map((t) => (
                      <option key={t} value={t}>{t === 'platform' ? 'platform (operator)' : t}</option>
                    ))}
                  </select>
                </label>
              )}
              <button onClick={signOut}>Sign out</button>
            </>
          ) : (
            <>
              <label>
                Tenant
                <input
                  placeholder="tenant-alpha"
                  autoComplete="off"
                  name="ecarmf-tenant-id"
                  list="ecarmf-known-tenants"
                  value={tenantInput}
                  onChange={(e) => setTenantInput(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && applyTenant()}
                />
                <datalist id="ecarmf-known-tenants">
                  {knownTenants.map((t) => (
                    <option key={t} value={t} />
                  ))}
                </datalist>
              </label>
              <button onClick={() => applyTenant()} disabled={!tenantInput.trim()}>Switch</button>
              <label>
                As
                <select value={user} onChange={(e) => switchUser(e.target.value)}>
                  {SEEDED_USERS.map((u) => (
                    <option key={u.id} value={u.id}>{u.label}</option>
                  ))}
                </select>
              </label>
              <label>
                Key
                <input
                  type="password"
                  placeholder="ecarmf_…"
                  autoComplete="off"
                  name="ecarmf-access-key"
                  value={apiKeyInput}
                  onChange={(e) => setApiKeyInput(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && apiKeyInput.trim() && signIn()}
                />
              </label>
              <button onClick={signIn} disabled={!apiKeyInput.trim()}>Sign in</button>
            </>
          )}
        </div>
      </header>

      {/* Ghost-tenant rescue: a warning from Switch validation, or a banner
          when the STORED tenant doesn't exist (e.g. 'jj') — one click home. */}
      {!signedInWithKey && tenantWarning && (
        <div className="error" style={{ margin: '0.5rem 1rem' }}>
          {tenantWarning}{' '}
          <button className="secondary" onClick={() => { setTenantWarning(null); applyTenant('platform') }}>
            Go to platform
          </button>
        </div>
      )}
      {!signedInWithKey && !tenantWarning && knownTenants.length > 0
        && !knownTenants.some((t) => t.toLowerCase() === tenant.toLowerCase()) && (
        <div className="error" style={{ margin: '0.5rem 1rem' }}>
          Tenant '{tenant}' does not exist — this workspace is empty. Known tenants: {knownTenants.join(', ')}.{' '}
          <button className="secondary" onClick={() => applyTenant('platform')}>Go to platform</button>
        </div>
      )}

      <div className="layout">
        {navOpen && <div className="backdrop" onClick={() => setNavOpen(false)} />}
        <aside className={`sidebar ${navOpen ? 'open' : ''}`}>
          {effectiveTenant && (
            <div className="tenant-badge" title="Every screen shows only this tenant's data.">
              <span className="muted small">Viewing tenant</span>
              <strong className="tenant-brand">{cfg.brand}</strong>
              {cfg.segment && <span className="muted small">{cfg.segment}</span>}
              <span className="tenant-chips">
                <span className="mono small">{effectiveTenant}</span>
                {cfg.posture !== 'standard' && (
                  <span className={`posture-chip posture-${cfg.posture}`}>
                    {cfg.phi ? 'HIPAA · PHI' : cfg.posture === 'regulated' ? 'Regulated' : 'Elevated'}
                  </span>
                )}
              </span>
            </div>
          )}
          {visibleNav.map((item, index) => (
            <span key={item.tab} style={{ display: 'contents' }}>
              {item.group && (index === 0 || visibleNav[index - 1].group !== item.group) && (
                <button
                  className="nav-group nav-group-toggle"
                  aria-expanded={!collapsedGroups.has(item.group)}
                  onClick={() => toggleGroup(item.group)}
                >
                  <span className="nav-chevron" aria-hidden><Icon name={collapsedGroups.has(item.group) ? 'chevron-right' : 'chevron-down'} size={12} /></span>
                  {item.group}
                </button>
              )}
              {(!item.group || !collapsedGroups.has(item.group)) && (
                <button
                  className={tab === item.tab ? 'active' : ''}
                  onClick={() => openTab(item.tab)}
                >
                  <Icon name={item.icon} /> {item.label}
                </button>
              )}
            </span>
          ))}
        </aside>

        <main className="content">
          <ScreenBoundary screen={`${tab}:${effectiveTenant}`}>
          {!effectiveTenant ? (
            <section className="panel">
              <h2>Step 0 — choose your tenant (or sign in)</h2>
              <p className="muted">
                The platform serves multiple clients; everything you see belongs to one tenant.
                Sign in with your issued access key — it identifies both you and your tenant — or,
                in development mode, type a tenant id in the top bar (e.g. <code>tenant-alpha</code>,
                or <code>platform</code> for the operator console) and press Switch.
              </p>
            </section>
          ) : isPlatformTab && !onPlatformTenant ? (
            <OperatorGate />
          ) : tab === 'home' ? (
            operator && onPlatformTenant ? (
              <OperatorHome onPick={(id) => { applyTenant(id); setTab('home') }} />
            ) : (
              <Home tenant={effectiveTenant} user={effectiveUser} go={setTab} />
            )
          ) : tab === 'systemmap' ? (
            // A guide, not tenant data — always available, even on the
            // operator tenant, so anyone can see how to move through the system.
            <SystemMap tenant={effectiveTenant} user={effectiveUser} go={setTab} />
          ) : tab === 'glossary' ? (
            <Glossary go={setTab} />
          ) : operator && onPlatformTenant && !isPlatformTab ? (
            <section className="panel">
              <h2>Nothing here on the operator tenant</h2>
              <p className="muted">
                <strong>{tab}</strong> shows a client's data, but you're on <code>platform</code> —
                the operator console, which has no business data of its own. Pick a client to see its{' '}
                {tab}.
              </p>
              <button onClick={() => setTab('home')}>← Choose a client</button>
            </section>
          ) : tab === 'organization' ? (
            <Organization tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'packages' ? (
            <PackageInspector tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'controls' ? (
            <ControlsExplorer tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'explore' ? (
            <CapabilityExplorer tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'agents' ? (
            <Agents tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'integrations' ? (
            <Integrations tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'benchmarks' ? (
            <Benchmarks tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'renewals' ? (
            <Renewals tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'treasury' ? (
            <Treasury tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'ai' ? (
            <AiSettings tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'dataentry' ? (
            <DataEntry tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'statements' ? (
            <StatementReview tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'activity' ? (
            <RecordActivity tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'dashboard' ? (
            <Dashboard tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'risk' ? (
            <RiskRegister tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'risktreatment' ? (
            <RiskTreatments tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'periods' ? (
            <Periods tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'cases' ? (
            <Cases tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'reports' ? (
            <Reports tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'boardpack' ? (
            <BoardPack tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'peer' ? (
            <PeerBenchmark tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'audit' ? (
            <AuditLog tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'library' ? (
            <Library tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'allocations' ? (
            <Allocations tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'advisor' ? (
            <Advisor tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'health' ? (
            <HealthBoard tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'catalog' ? (
            <PackageCatalog tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'skills' ? (
            <Skills tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'skillslibrary' ? (
            <SkillsLibrary />
          ) : tab === 'actions' ? (
            <PlatformActions onOpenTenant={(id, t) => {
              setKnownTenants((prev) => prev.includes(id) ? prev : [...prev, id])
              setTenant(id); setTenantState(id); setTenantInput(id); setTab(t)
            }} />
          ) : tab === 'platformrisk' ? (
            <PlatformRisk onOpenTenant={(id) => {
              setKnownTenants((prev) => prev.includes(id) ? prev : [...prev, id])
              setTenant(id); setTenantState(id); setTenantInput(id); setTab('risk')
            }} />
          ) : tab === 'demos' ? (
            <Demos onOpen={(id) => {
              setKnownTenants((prev) => prev.includes(id) ? prev : [...prev, id])
              setTenant(id); setTenantState(id); setTenantInput(id); setTab('home')
            }} />
          ) : tab === 'enroll' ? (
            <EnrollTenant onProvisioned={(id) => {
              // Freshly created and valid — switch directly (the prefix-guard
              // in applyTenant would reject it against the startup-stale list)
              // and register it so the switcher/autocomplete knows it.
              setKnownTenants((prev) => prev.includes(id) ? prev : [...prev, id])
              setTenant(id); setTenantState(id); setTenantInput(id)
              setTab('home')
            }} />
          ) : tab === 'clients' ? (
            <Clients tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'email' ? (
            <EmailSettings tenant={effectiveTenant} user={effectiveUser} />
          ) : (
            <Billing tenant={effectiveTenant} user={effectiveUser} />
          )}
          </ScreenBoundary>
        </main>
      </div>

      {paletteOpen && effectiveTenant && (
        <CommandPalette
          tenant={effectiveTenant}
          isPlatform={onPlatformTenant}
          go={(t) => { openTab(t); setPaletteOpen(false) }}
          onOpenTenant={(id) => {
            setKnownTenants((prev) => prev.includes(id) ? prev : [...prev, id])
            setTenant(id); setTenantState(id); setTenantInput(id); setTab('home')
            setPaletteOpen(false)
          }}
          onClose={() => setPaletteOpen(false)}
        />
      )}
    </div>
  )
}

export default App
