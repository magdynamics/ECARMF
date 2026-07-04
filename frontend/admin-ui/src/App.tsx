import { useState } from 'react'
import './App.css'
import { getTenant, setTenant } from './api'
import { Dashboard } from './components/Dashboard'
import { PackageInspector } from './components/PackageInspector'
import { RecordActivity } from './components/RecordActivity'

type Tab = 'dashboard' | 'packages' | 'activity'

function App() {
  const [tenant, setTenantState] = useState(getTenant())
  const [tenantInput, setTenantInput] = useState(tenant)
  const [tab, setTab] = useState<Tab>('packages')

  function applyTenant() {
    const value = tenantInput.trim()
    setTenant(value)
    setTenantState(value)
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
          {tenant && (
            <span className="muted small">
              acting as <strong>{tenant}</strong>
            </span>
          )}
        </div>
      </header>

      {!tenant ? (
        <section className="panel">
          <h2>Select a tenant</h2>
          <p className="muted">
            The platform serves multiple clients. Enter a tenant id above — every package,
            transaction, and audit record you see belongs to that tenant only.
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
          </nav>
          {tab === 'dashboard' ? (
            <Dashboard tenant={tenant} />
          ) : tab === 'packages' ? (
            <PackageInspector tenant={tenant} />
          ) : (
            <RecordActivity tenant={tenant} />
          )}
        </>
      )}
    </div>
  )
}

export default App
