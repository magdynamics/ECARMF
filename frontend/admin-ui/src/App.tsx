import { useState } from 'react'
import './App.css'
import { getTenant, getUser, setTenant, setUser } from './api'
import { Allocations } from './components/Allocations'
import { Dashboard } from './components/Dashboard'
import { DataEntry } from './components/DataEntry'
import { Home } from './components/Home'
import { PackageInspector } from './components/PackageInspector'
import { RecordActivity } from './components/RecordActivity'

const SEEDED_USERS = [
  { id: 'owner@platform', label: 'Owner / Executive' },
  { id: 'admin@platform', label: 'Platform Administrator' },
]

// Navigation is grouped by what the user is doing: getting oriented,
// setting up, putting data IN, or reading results OUT.
const NAV: { tab: string; label: string; group: string }[] = [
  { tab: 'home', label: 'Start Here', group: '' },
  { tab: 'packages', label: 'Packages', group: 'Setup' },
  { tab: 'dataentry', label: 'Data Entry', group: 'Input' },
  { tab: 'activity', label: 'Record Activity', group: 'Output' },
  { tab: 'dashboard', label: 'Dashboard', group: 'Output' },
  { tab: 'allocations', label: 'Allocations', group: 'Output' },
]

function App() {
  const [tenant, setTenantState] = useState(getTenant())
  const [tenantInput, setTenantInput] = useState(tenant)
  const [user, setUserState] = useState(getUser())
  const [tab, setTab] = useState('home')

  function applyTenant() {
    const value = tenantInput.trim()
    setTenant(value)
    setTenantState(value)
  }

  function switchUser(id: string) {
    setUser(id)
    setUserState(id)
  }

  return (
    <div className="app">
      <header>
        <h1>
          ECARMF <span className="accent">Platform Kernel</span>
        </h1>
        <div className="tenant-bar">
          <label>
            Tenant
            <input
              placeholder="e.g. tenant-alpha"
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
        </div>
      </header>

      {!tenant ? (
        <section className="panel">
          <h2>Step 0 — choose your tenant</h2>
          <p className="muted">
            The platform serves multiple clients; everything you see belongs to one tenant. Type a
            tenant id above (e.g. <code>tenant-alpha</code>) and press Switch. Then the Start Here
            page walks you through the rest: what to set up, where data goes in, and where the
            results come out.
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
            <Home tenant={tenant} user={user} go={setTab} />
          ) : tab === 'packages' ? (
            <PackageInspector tenant={tenant} user={user} />
          ) : tab === 'dataentry' ? (
            <DataEntry tenant={tenant} user={user} />
          ) : tab === 'activity' ? (
            <RecordActivity tenant={tenant} user={user} />
          ) : tab === 'dashboard' ? (
            <Dashboard tenant={tenant} user={user} />
          ) : (
            <Allocations tenant={tenant} user={user} />
          )}
        </>
      )}
    </div>
  )
}

export default App
