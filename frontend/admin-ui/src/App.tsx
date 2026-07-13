import { Component, useEffect, useState, type ReactNode } from 'react'
import './App.css'
import { api, getApiKey, getTenant, getUser, setApiKey, setTenant, setUser } from './api'
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
import { Integrations } from './components/Integrations'
import { Library } from './components/Library'
import { Organization } from './components/Organization'
import { PackageInspector } from './components/PackageInspector'
import { RecordActivity } from './components/RecordActivity'
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
  { tab: 'home', label: 'Start Here', icon: '🏠', group: '' },
  { tab: 'organization', label: 'Organization', icon: '🏛️', group: 'Setup' },
  { tab: 'packages', label: 'Packages', icon: '📦', group: 'Setup' },
  { tab: 'integrations', label: 'Integrations', icon: '🔌', group: 'Setup' },
  { tab: 'benchmarks', label: 'Benchmarks', icon: '🎯', group: 'Setup' },
  { tab: 'renewals', label: 'Renewals', icon: '📅', group: 'Setup' },
  { tab: 'treasury', label: 'AI Treasury', icon: '🏦', group: 'Setup' },
  { tab: 'ai', label: 'AI Backend', icon: '🧠', group: 'Setup' },
  { tab: 'dataentry', label: 'Data Entry', icon: '📥', group: 'Input' },
  { tab: 'statements', label: 'Statement Review', icon: '🧐', group: 'Input' },
  { tab: 'activity', label: 'Record Activity', icon: '📋', group: 'Output' },
  { tab: 'dashboard', label: 'Dashboard', icon: '📊', group: 'Output' },
  { tab: 'reports', label: 'Reports', icon: '📑', group: 'Output' },
  { tab: 'library', label: 'Library', icon: '🗄️', group: 'Output' },
  { tab: 'allocations', label: 'Capital Flows', icon: '💼', group: 'Output' },
  { tab: 'advisor', label: 'AI Advisor', icon: '🤖', group: 'Output' },
  { tab: 'health', label: 'Health Board', icon: '🩺', group: 'Platform' },
  { tab: 'clients', label: 'Clients', icon: '🏢', group: 'Platform' },
  { tab: 'billing', label: 'Billing', icon: '🧾', group: 'Platform' },
  { tab: 'email', label: 'Email', icon: '✉️', group: 'Platform' },
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

  function openTab(next: string) {
    setTab(next)
    setNavOpen(false)
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
  const isPlatformTab = tab === 'clients' || tab === 'billing' || tab === 'email' || tab === 'health'
  const onPlatformTenant = effectiveTenant.toLowerCase() === 'platform'

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

  return (
    <div className="app">
      <header className="topbar">
        <div className="brand">
          <button className="menu-toggle" onClick={() => setNavOpen((o) => !o)} aria-label="Menu">☰</button>
          <h1>
            ECARMF <span className="accent">Platform Kernel</span>
          </h1>
        </div>
        <div className="tenant-bar">
          {signedInWithKey ? (
            <>
              <span className="small">
                <strong>{me?.displayName ?? '…'}</strong>{' '}
                <span className="muted">· {operator ? effectiveTenant : (me?.tenantName ?? me?.tenantId)}</span>
              </span>
              {operator && (
                <>
                  <label>
                    View
                    <input
                      placeholder="platform"
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
                </>
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
              <strong className="mono">{effectiveTenant}</strong>
            </div>
          )}
          {NAV.map((item, index) => (
            <span key={item.tab} style={{ display: 'contents' }}>
              {item.group && (index === 0 || NAV[index - 1].group !== item.group) && (
                <div className="nav-group">{item.group}</div>
              )}
              <button
                className={tab === item.tab ? 'active' : ''}
                onClick={() => openTab(item.tab)}
              >
                <span>{item.icon}</span> {item.label}
              </button>
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
            <Home tenant={effectiveTenant} user={effectiveUser} go={setTab} />
          ) : tab === 'organization' ? (
            <Organization tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'packages' ? (
            <PackageInspector tenant={effectiveTenant} user={effectiveUser} />
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
          ) : tab === 'reports' ? (
            <Reports tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'library' ? (
            <Library tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'allocations' ? (
            <Allocations tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'advisor' ? (
            <Advisor tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'health' ? (
            <HealthBoard tenant={effectiveTenant} user={effectiveUser} />
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
    </div>
  )
}

export default App
