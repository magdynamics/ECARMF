import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api'

interface OrgUnit {
  unitId: string
  name: string
  unitType: string
  parentUnitId: string | null
  industry: string | null
  attachedPackageIds: string[]
  notes: string | null
  status: string
}

interface PackageInfo {
  manifest: { packageId: string; name: string }
  state: string
}

interface Suggestion {
  frameworkId: string
  name: string
  packageId: string
  packageVersion: string
  alreadyAttached: boolean
}

/// The tenant's organizational shape: build any hierarchy (Division →
/// Location → Property, Region → Project — whatever fits), attach packages
/// per unit, and let the recommender suggest frameworks by industry.
export function Organization({ tenant, user }: { tenant: string; user: string }) {
  const [units, setUnits] = useState<OrgUnit[]>([])
  const [packages, setPackages] = useState<PackageInfo[]>([])
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [selected, setSelected] = useState<string | null>(null)
  const [suggestions, setSuggestions] = useState<Suggestion[] | null>(null)
  const [attachChoice, setAttachChoice] = useState('')
  const [form, setForm] = useState({
    unitId: '', name: '', unitType: 'Location', parentUnitId: '', industry: '',
  })

  const fail = (e: unknown) => setError(e instanceof ApiError ? e.message : String(e))

  const load = useCallback(async () => {
    try {
      setUnits(await api.get<OrgUnit[]>('/api/org-units'))
      setError(null)
    } catch (e) {
      fail(e)
    }
    try {
      setPackages((await api.get<PackageInfo[]>('/api/packages')).filter((p) => p.state === 'Active'))
    } catch {
      setPackages([])
    }
  }, [])

  useEffect(() => {
    void load()
    setSelected(null)
    setSuggestions(null)
  }, [load, tenant, user])

  // Flatten the tree depth-first so the table shows structure by indent.
  function ordered(): { unit: OrgUnit; depth: number }[] {
    const byParent = new Map<string | null, OrgUnit[]>()
    for (const u of units) {
      const key = u.parentUnitId ?? null
      byParent.set(key, [...(byParent.get(key) ?? []), u])
    }
    const result: { unit: OrgUnit; depth: number }[] = []
    const walk = (parent: string | null, depth: number) => {
      for (const u of byParent.get(parent) ?? []) {
        result.push({ unit: u, depth })
        walk(u.unitId, depth + 1)
      }
    }
    walk(null, 0)
    // Orphans (parent not loaded) still show rather than vanish.
    for (const u of units) if (!result.some((r) => r.unit.unitId === u.unitId)) result.push({ unit: u, depth: 0 })
    return result
  }

  async function create() {
    setError(null)
    setMessage(null)
    try {
      await api.post('/api/org-units', {
        unitId: form.unitId,
        name: form.name,
        unitType: form.unitType,
        parentUnitId: form.parentUnitId || null,
        industry: form.industry || null,
        notes: null,
      })
      setMessage(`Unit '${form.name}' added${form.parentUnitId ? ` under '${form.parentUnitId}'` : ' at the root'}.`)
      setForm({ ...form, unitId: '', name: '', industry: '' })
      await load()
    } catch (e) {
      fail(e)
    }
  }

  async function remove(unitId: string) {
    setError(null)
    try {
      await api.delete(`/api/org-units/${unitId}`)
      if (selected === unitId) setSelected(null)
      await load()
    } catch (e) {
      fail(e)
    }
  }

  async function toggleDetail(u: OrgUnit) {
    setSuggestions(null)
    setAttachChoice('')
    if (selected === u.unitId) {
      setSelected(null)
      return
    }
    setSelected(u.unitId)
    if (u.industry) {
      try {
        const s = await api.get<{ frameworks: Suggestion[] }>(`/api/org-units/${u.unitId}/suggestions`)
        setSuggestions(s.frameworks)
      } catch (e) {
        fail(e)
      }
    }
  }

  async function attach(unitId: string, packageId: string) {
    setError(null)
    try {
      await api.post(`/api/org-units/${unitId}/packages`, { packageId })
      setMessage(`'${packageId}' now runs for unit '${unitId}'.`)
      await load()
    } catch (e) {
      fail(e)
    }
  }

  async function detach(unitId: string, packageId: string) {
    try {
      await api.delete(`/api/org-units/${unitId}/packages/${packageId}`)
      await load()
    } catch (e) {
      fail(e)
    }
  }

  const rows = ordered()
  const selectedUnit = units.find((u) => u.unitId === selected)

  return (
    <div>
      {message && <div className="banner banner-ok">{message}</div>}
      {error && <div className="banner banner-error">{error}</div>}

      <section className="panel">
        <h2>Organization <span className="state state-staged">SETUP</span></h2>
        <p className="muted small">
          Your business's real shape, whatever it is — Division → Legal Entity → Location →
          Property, Region → Project, anything, any depth. The hierarchy is data, not schema.
          Attach knowledge packages per unit so each part of the business runs exactly the
          intelligence that fits it; declare an industry and the platform suggests frameworks.
        </p>

        <table>
          <thead>
            <tr><th>Unit</th><th>Type</th><th>Industry</th><th>Packages</th><th></th></tr>
          </thead>
          <tbody>
            {rows.map(({ unit, depth }) => (
              <tr key={unit.unitId} className={selected === unit.unitId ? 'selected' : ''}>
                <td style={{ paddingLeft: `${0.5 + depth * 1.4}rem` }}>
                  {depth > 0 && <span className="muted">└ </span>}
                  <strong>{unit.name}</strong>{' '}
                  <span className="muted mono small">{unit.unitId}</span>
                </td>
                <td>{unit.unitType}</td>
                <td>{unit.industry ?? '—'}</td>
                <td className="small">
                  {unit.attachedPackageIds.length === 0
                    ? <span className="muted">tenant-wide only</span>
                    : unit.attachedPackageIds.join(', ')}
                </td>
                <td style={{ whiteSpace: 'nowrap' }}>
                  <button className="secondary" onClick={() => toggleDetail(unit)}>
                    {selected === unit.unitId ? 'Close' : 'Manage'}
                  </button>{' '}
                  <button className="secondary" onClick={() => remove(unit.unitId)}>Delete</button>
                </td>
              </tr>
            ))}
            {rows.length === 0 && (
              <tr><td colSpan={5} className="muted">
                No units yet — model your organization below, starting from a root unit.
              </td></tr>
            )}
          </tbody>
        </table>

        {selectedUnit && (
          <div className="card">
            <strong>{selectedUnit.name}</strong> <span className="muted small">— attached packages</span>
            <div className="small" style={{ margin: '0.4rem 0' }}>
              {selectedUnit.attachedPackageIds.map((p) => (
                <span key={p} className="state state-staged" style={{ marginRight: '0.4rem' }}>
                  {p} <a onClick={() => detach(selectedUnit.unitId, p)} style={{ cursor: 'pointer' }}>✕</a>
                </span>
              ))}
              {selectedUnit.attachedPackageIds.length === 0 && <span className="muted">none — this unit inherits only tenant-wide behavior</span>}
            </div>
            <div className="form-row">
              <label>Attach package<select value={attachChoice} onChange={(e) => setAttachChoice(e.target.value)}>
                <option value="">choose an active package…</option>
                {packages.map((p) => (
                  <option key={p.manifest.packageId} value={p.manifest.packageId}>
                    {p.manifest.name} ({p.manifest.packageId})
                  </option>
                ))}
              </select></label>
              <button onClick={() => attach(selectedUnit.unitId, attachChoice)} disabled={!attachChoice}>Attach</button>
            </div>
            {suggestions && suggestions.length > 0 && (
              <>
                <strong className="small">Suggested for industry '{selectedUnit.industry}'</strong>
                {suggestions.map((s) => (
                  <div key={s.frameworkId} className="small" style={{ padding: '0.15rem 0' }}>
                    🎯 {s.name} <span className="muted mono">({s.packageId})</span>{' '}
                    {s.alreadyAttached
                      ? <span className="state state-approved">attached</span>
                      : <button className="secondary" onClick={() => attach(selectedUnit.unitId, s.packageId)}>Attach</button>}
                  </div>
                ))}
              </>
            )}
            {suggestions && suggestions.length === 0 && selectedUnit.industry && (
              <p className="muted small">No frameworks match industry '{selectedUnit.industry}' among active packages.</p>
            )}
          </div>
        )}

        <h3>Add a unit</h3>
        <div className="form-row">
          <label>Unit id<input placeholder="orland-park-clinic" value={form.unitId}
            onChange={(e) => setForm({ ...form, unitId: e.target.value })} /></label>
          <label>Name<input placeholder="Orland Park Clinic" value={form.name}
            onChange={(e) => setForm({ ...form, name: e.target.value })} /></label>
          <label>Type<input placeholder="Division / Location / Property / Project…" value={form.unitType}
            onChange={(e) => setForm({ ...form, unitType: e.target.value })} /></label>
          <label>Parent<select value={form.parentUnitId}
            onChange={(e) => setForm({ ...form, parentUnitId: e.target.value })}>
            <option value="">(root)</option>
            {units.map((u) => <option key={u.unitId} value={u.unitId}>{u.name}</option>)}
          </select></label>
          <label>Industry<input placeholder="dental / restaurant / real-estate…" value={form.industry}
            onChange={(e) => setForm({ ...form, industry: e.target.value })} /></label>
          <button onClick={create} disabled={!form.unitId.trim() || !form.name.trim() || !form.unitType.trim()}>
            Add unit
          </button>
        </div>
      </section>
    </div>
  )
}
