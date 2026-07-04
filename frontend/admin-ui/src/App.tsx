import { useState } from 'react'
import './App.css'
import { getTenant, getUser, setTenant, setUser } from './api'
import { Allocations } from './components/Allocations'
import { Dashboard } from './components/Dashboard'
import { PackageInspector } from './components/PackageInspector'
import { RecordActivity } from './components/RecordActivity'

type Tab = 'dashboard' | 'packages' | 'activity' | 'allocations'

const SEEDED_USERS = [
  { id: 'owner@platform', label: 'Owner / Executive' },
  { id: 'admin@platform', label: 'Platform Administrator' },
]

function App() {
  const [tenant, setTenantState] = useState(getTenant())
  const [tenantInput, setTenantInput] = useState(tenant)
  const [user, setUserState] = useState(getUser())
  const [tab, setTab] = useState<Tab>('dashboard')

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
          <h2>Select a tenant</h2>
          <p className="muted">
            The platform serves multiple clients. Enter a tenant id above — every package,
            record, score, and audit entry you see belongs to that tenant only. Use the
            identity switcher to demonstrate role enforcement: the Administrator manages
            packages and connectors but cannot approve; the Owner decides.
          </p>
        </section>
      ) : (
        <>
          <nav className="tabs">
            <button className={tab === 'dashboard' ? 'active' : ''} onClick={() => setTab('dashboard')}>
              Dashboard
            </button>
            <button className={tab === 'packages' ? 'active' : ''} onClick={() => setTab('packages')}>
              Package Inspector
            </button>
            <button className={tab === 'activity' ? 'active' : ''} onClick={() => setTab('activity')}>
              Record Activity
            </button>
            <button className={tab === 'allocations' ? 'active' : ''} onClick={() => setTab('allocations')}>
              Allocations
            </button>
          </nav>
          {tab === 'dashboard' ? (
            <Dashboard tenant={tenant} user={user} />
          ) : tab === 'packages' ? (
            <PackageInspector tenant={tenant} user={user} />
          ) : tab === 'activity' ? (
            <RecordActivity tenant={tenant} user={user} />
          ) : (
            <Allocations tenant={tenant} user={user} />
          )}
        </>
      )}
    </div>
  )
}

export default App
