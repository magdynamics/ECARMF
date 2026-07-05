import { useEffect, useState } from 'react'
import './App.css'
import { api, getApiKey, getTenant, getUser, setApiKey, setTenant, setUser } from './api'
import { Advisor } from './components/Advisor'
import { AiSettings } from './components/AiSettings'
import { Allocations } from './components/Allocations'
import { Benchmarks } from './components/Benchmarks'
import { Clients } from './components/Clients'
import { Dashboard } from './components/Dashboard'
import { DataEntry } from './components/DataEntry'
import { Home } from './components/Home'
import { Integrations } from './components/Integrations'
import { Library } from './components/Library'
import { PackageInspector } from './components/PackageInspector'
import { RecordActivity } from './components/RecordActivity'

const SEEDED_USERS = [
  { id: 'owner@platform', label: 'Owner / Executive' },
  { id: 'admin@platform', label: 'Platform Administrator' },
]

// Navigation grouped by what the user is doing: getting oriented, setting up,
// putting data IN, reading results OUT — plus the operator-only Platform group.
const NAV: { tab: string; label: string; group: string }[] = [
  { tab: 'home', label: 'Start Here', group: '' },
  { tab: 'packages', label: 'Packages', group: 'Setup' },
  { tab: 'integrations', label: 'Integrations', group: 'Setup' },
  { tab: 'benchmarks', label: 'Benchmarks', group: 'Setup' },
  { tab: 'ai', label: 'AI Backend', group: 'Setup' },
  { tab: 'dataentry', label: 'Data Entry', group: 'Input' },
  { tab: 'activity', label: 'Record Activity', group: 'Output' },
  { tab: 'dashboard', label: 'Dashboard', group: 'Output' },
  { tab: 'library', label: 'Library', group: 'Output' },
  { tab: 'allocations', label: 'Allocations', group: 'Output' },
  { tab: 'advisor', label: 'AI Advisor', group: 'Output' },
  { tab: 'clients', label: 'Clients', group: 'Platform' },
]

interface Me {
  tenantId: string
  tenantName: string
  identifier: string
  displayName: string
  viaApiKey: boolean
  isPlatformOperator: boolean
}

function App() {
  const [tenant, setTenantState] = useState(getTenant())
  const [tenantInput, setTenantInput] = useState(tenant)
  const [user, setUserState] = useState(getUser())
  const [apiKeyInput, setApiKeyInput] = useState('')
  const [me, setMe] = useState<Me | null>(null)
  const [signedInWithKey, setSignedInWithKey] = useState(!!getApiKey())
  const [tab, setTab] = useState('home')

  useEffect(() => {
    if (!signedInWithKey) {
      setMe(null)
      return
    }
    api
      .get<Me>('/api/me')
      .then((m) => {
        setMe(m)
        setTenantState(m.tenantId)
        setUserState(m.identifier)
      })
      .catch(() => {
        // invalid/revoked key — drop it
        setApiKey('')
        setSignedInWithKey(false)
      })
  }, [signedInWithKey])

  function applyTenant() {
    const value = tenantInput.trim()
    setTenant(value)
    setTenantState(value)
  }

  function switchUser(id: string) {
    setUser(id)
    setUserState(id)
  }

  function signIn() {
    setApiKey(apiKeyInput.trim())
    setApiKeyInput('')
    setSignedInWithKey(true)
  }

  function signOut() {
    setApiKey('')
    setSignedInWithKey(false)
    setMe(null)
  }

  const effectiveTenant = signedInWithKey ? (me?.tenantId ?? '') : tenant
  const effectiveUser = signedInWithKey ? (me?.identifier ?? '') : user

  return (
    <div className="app">
      <header>
        <h1>
          ECARMF <span className="accent">Platform Kernel</span>
        </h1>
        <div className="tenant-bar">
          {signedInWithKey ? (
            <>
              <span className="small">
                Signed in as <strong>{me?.displayName ?? '…'}</strong>{' '}
                <span className="muted">({me?.identifier} · tenant {me?.tenantName ?? me?.tenantId})</span>
              </span>
              <button onClick={signOut}>Sign out</button>
            </>
          ) : (
            <>
              <label>
                Tenant
                <input
                  placeholder="e.g. tenant-alpha or platform"
                  value={tenantInput}
                  onChange={(e) => setTenantInput(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && applyTenant()}
                />
              </label>
              <button onClick={applyTenant} disabled={!tenantInput.trim()}>
                Switch
              </button>
              <label>
                Acting as
                <select value={user} onChange={(e) => switchUser(e.target.value)}>
                  {SEEDED_USERS.map((u) => (
                    <option key={u.id} value={u.id}>
                      {u.label}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Access key
                <input
                  type="password"
                  placeholder="ecarmf_…"
                  value={apiKeyInput}
                  onChange={(e) => setApiKeyInput(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && apiKeyInput.trim() && signIn()}
                />
              </label>
              <button onClick={signIn} disabled={!apiKeyInput.trim()}>
                Sign in
              </button>
            </>
          )}
        </div>
      </header>

      {!effectiveTenant ? (
        <section className="panel">
          <h2>Step 0 — choose your tenant (or sign in)</h2>
          <p className="muted">
            The platform serves multiple clients; everything you see belongs to one tenant. Sign in
            with your issued access key — it identifies both you and your tenant — or, in
            development mode, type a tenant id above (e.g. <code>tenant-alpha</code>, or{' '}
            <code>platform</code> for the operator console) and press Switch.
          </p>
        </section>
      ) : (
        <>
          <nav className="tabs">
            {NAV.map((item) => (
              <span key={item.tab} className="tab-item">
                {item.group && <span className="tab-group">{item.group}</span>}
                <button className={tab === item.tab ? 'active' : ''} onClick={() => setTab(item.tab)}>
                  {item.label}
                </button>
              </span>
            ))}
          </nav>
          {tab === 'home' ? (
            <Home tenant={effectiveTenant} user={effectiveUser} go={setTab} />
          ) : tab === 'packages' ? (
            <PackageInspector tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'integrations' ? (
            <Integrations tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'benchmarks' ? (
            <Benchmarks tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'ai' ? (
            <AiSettings tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'dataentry' ? (
            <DataEntry tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'activity' ? (
            <RecordActivity tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'dashboard' ? (
            <Dashboard tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'library' ? (
            <Library tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'allocations' ? (
            <Allocations tenant={effectiveTenant} user={effectiveUser} />
          ) : tab === 'advisor' ? (
            <Advisor tenant={effectiveTenant} user={effectiveUser} />
          ) : (
            <Clients tenant={effectiveTenant} user={effectiveUser} />
          )}
        </>
      )}
    </div>
  )
}

export default App
