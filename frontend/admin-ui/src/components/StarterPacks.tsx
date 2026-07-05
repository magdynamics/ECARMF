import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api'

interface TemplateSummary {
  templateId: string
  name: string
  industry: string | null
  description: string | null
  createdFromTenant: string
  createdBy: string
  createdAt: string
  packageCount: number
  benchmarkCount: number
  renewalCount: number
}

interface TenantOption {
  tenantId: string
  name: string
}

/// Industry starter packs: capture a well-configured client as a template,
/// apply it to every new client of that kind in one click.
export function StarterPacks() {
  const [templates, setTemplates] = useState<TemplateSummary[]>([])
  const [tenants, setTenants] = useState<TenantOption[]>([])
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [capture, setCapture] = useState({ templateId: '', name: '', industry: '', fromTenantId: '' })
  const [applyTo, setApplyTo] = useState<Record<string, string>>({})

  const fail = (e: unknown) => setError(e instanceof ApiError ? e.message : String(e))

  const load = useCallback(async () => {
    try {
      const [t, ten] = await Promise.all([
        api.get<TemplateSummary[]>('/api/platform/templates'),
        api.get<TenantOption[]>('/api/platform/tenants'),
      ])
      setTemplates(t)
      setTenants(ten)
      setError(null)
    } catch (e) {
      fail(e)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  async function captureTemplate() {
    setError(null)
    setMessage(null)
    setBusy(true)
    try {
      const summary = await api.post<TemplateSummary>('/api/platform/templates/capture', {
        templateId: capture.templateId,
        name: capture.name,
        industry: capture.industry || null,
        description: null,
        fromTenantId: capture.fromTenantId,
      })
      setMessage(
        `Template '${summary.name}' captured from ${summary.createdFromTenant}: ` +
        `${summary.packageCount} package(s), ${summary.benchmarkCount} benchmark(s), ${summary.renewalCount} renewal(s).`)
      setCapture({ templateId: '', name: '', industry: '', fromTenantId: '' })
      await load()
    } catch (e) {
      fail(e)
    } finally {
      setBusy(false)
    }
  }

  async function apply(t: TemplateSummary) {
    const target = applyTo[t.templateId]
    if (!target) return
    setError(null)
    setMessage(null)
    setBusy(true)
    try {
      const result = await api.post<{
        packagesActivated: string[]
        packagesSkipped: string[]
        benchmarksCreated: number
        renewalsCreated: number
        errors: string[]
      }>(`/api/platform/templates/${t.templateId}/apply`, { tenantId: target })
      setMessage(
        `'${t.name}' applied to ${target}: ${result.packagesActivated.length} package(s) activated` +
        (result.packagesSkipped.length ? ` (${result.packagesSkipped.length} already present)` : '') +
        `, ${result.benchmarksCreated} benchmark(s), ${result.renewalsCreated} renewal(s).` +
        (result.errors.length ? ` Errors: ${result.errors.join(' | ')}` : ''))
    } catch (e) {
      fail(e)
    } finally {
      setBusy(false)
    }
  }

  async function remove(templateId: string) {
    try {
      await api.delete(`/api/platform/templates/${templateId}`)
      await load()
    } catch (e) {
      fail(e)
    }
  }

  return (
    <section className="panel">
      <h2>Starter packs <span className="state state-staged">PLATFORM</span></h2>
      <p className="muted small">
        Scale onboarding: configure one client of a kind well, capture the setup as a template
        (its active packages, benchmarks, and renewal ladder), then apply it to each new client
        of that kind in one click. Applying is additive — nothing already configured is touched.
      </p>

      {message && <div className="banner banner-ok">{message}</div>}
      {error && <div className="banner banner-error">{error}</div>}

      <table>
        <thead>
          <tr><th>Template</th><th>Industry</th><th>Contains</th><th>From</th><th>Apply to</th><th></th></tr>
        </thead>
        <tbody>
          {templates.map((t) => (
            <tr key={t.templateId}>
              <td><strong>{t.name}</strong> <span className="muted mono small">{t.templateId}</span></td>
              <td>{t.industry ?? '—'}</td>
              <td className="small">{t.packageCount} pkg · {t.benchmarkCount} benchmarks · {t.renewalCount} renewals</td>
              <td className="small mono">{t.createdFromTenant}</td>
              <td>
                <select value={applyTo[t.templateId] ?? ''}
                  onChange={(e) => setApplyTo({ ...applyTo, [t.templateId]: e.target.value })}>
                  <option value="">choose client…</option>
                  {tenants.map((x) => <option key={x.tenantId} value={x.tenantId}>{x.name} ({x.tenantId})</option>)}
                </select>
              </td>
              <td>
                <button onClick={() => apply(t)} disabled={busy || !applyTo[t.templateId]}>Apply</button>{' '}
                <button className="secondary" onClick={() => remove(t.templateId)}>Delete</button>
              </td>
            </tr>
          ))}
          {templates.length === 0 && (
            <tr><td colSpan={6} className="muted">
              No templates yet — capture your best-configured client below.
            </td></tr>
          )}
        </tbody>
      </table>

      <h3>Capture a template from an existing client</h3>
      <div className="form-row">
        <label>Template id<input placeholder="restaurant" value={capture.templateId}
          onChange={(e) => setCapture({ ...capture, templateId: e.target.value })} /></label>
        <label>Name<input placeholder="Restaurant pack" value={capture.name}
          onChange={(e) => setCapture({ ...capture, name: e.target.value })} /></label>
        <label>Industry<input placeholder="Hospitality" value={capture.industry}
          onChange={(e) => setCapture({ ...capture, industry: e.target.value })} /></label>
        <label>From client<select value={capture.fromTenantId}
          onChange={(e) => setCapture({ ...capture, fromTenantId: e.target.value })}>
          <option value="">choose…</option>
          {tenants.map((x) => <option key={x.tenantId} value={x.tenantId}>{x.name} ({x.tenantId})</option>)}
        </select></label>
        <button onClick={captureTemplate}
          disabled={busy || !capture.templateId.trim() || !capture.name.trim() || !capture.fromTenantId}>
          Capture
        </button>
      </div>
    </section>
  )
}
