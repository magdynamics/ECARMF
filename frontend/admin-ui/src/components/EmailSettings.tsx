import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api'

interface MailStatus {
  configured: boolean
  enabled: boolean
  host: string | null
  port: number | null
  useSsl: boolean | null
  username: string | null
  fromAddress: string | null
  minSeverity: string | null
  configuredBy: string | null
  updatedAt: string | null
}

/// Platform mail delivery: point the kernel at your SMTP server and alarms
/// reach inboxes — benchmark breaches, renewal warnings — not just the bell.
export function EmailSettings({ tenant, user }: { tenant: string; user: string }) {
  const [status, setStatus] = useState<MailStatus | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [testTo, setTestTo] = useState('')
  const [busy, setBusy] = useState(false)
  const [form, setForm] = useState({
    enabled: true, host: '', port: '25', useSsl: false,
    username: '', password: '', fromAddress: '', minSeverity: 'Warning',
  })

  const fail = (e: unknown) => setError(e instanceof ApiError ? e.message : String(e))

  const load = useCallback(async () => {
    try {
      const s = await api.get<MailStatus>('/api/platform/mail')
      setStatus(s)
      if (s.configured) {
        setForm((f) => ({
          ...f,
          enabled: s.enabled,
          host: s.host ?? '',
          port: String(s.port ?? 25),
          useSsl: s.useSsl ?? false,
          username: s.username ?? '',
          fromAddress: s.fromAddress ?? '',
          minSeverity: s.minSeverity ?? 'Warning',
        }))
      }
      setError(null)
    } catch (e) {
      fail(e)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load, tenant, user])

  async function save() {
    setError(null)
    setMessage(null)
    setBusy(true)
    try {
      await api.put('/api/platform/mail', {
        enabled: form.enabled,
        host: form.host,
        port: Number(form.port),
        useSsl: form.useSsl,
        username: form.username || null,
        password: form.password || null,
        fromAddress: form.fromAddress,
        minSeverity: form.minSeverity,
      })
      setMessage(form.enabled
        ? `Mail delivery active via ${form.host}:${form.port} — ${form.minSeverity}+ alerts will be emailed within a minute of being raised.`
        : 'Settings saved; delivery is disabled (alerts stay in-app).')
      setForm((f) => ({ ...f, password: '' }))
      await load()
    } catch (e) {
      fail(e)
    } finally {
      setBusy(false)
    }
  }

  async function sendTest() {
    setError(null)
    setMessage(null)
    setBusy(true)
    try {
      const r = await api.post<{ sent: boolean; to: string }>('/api/platform/mail/test', { to: testTo })
      setMessage(`Test message sent to ${r.to} — check the inbox.`)
    } catch (e) {
      fail(e)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div>
      {message && <div className="banner banner-ok">{message}</div>}
      {error && <div className="banner banner-error">{error}</div>}

      <section className="panel">
        <h2>Email alert delivery <span className="state state-staged">PLATFORM</span></h2>
        <p className="muted small">
          Point the platform at your SMTP server (an on-prem relay or your office mail server —
          no external service) and alarms leave the app: a benchmark breach or a renewal warning
          reaches the client's inbox within a minute. Recipients are the tenant users holding the
          alerted role who have an email on file, falling back to the tenant's primary contact.
          Below the chosen minimum severity, alerts stay in-app only.
        </p>

        {status && (
          <p className="small">
            {status.configured ? (
              <>
                <span className={`state ${status.enabled ? 'state-active' : 'state-deactivated'}`}>
                  {status.enabled ? 'DELIVERY ON' : 'DELIVERY OFF'}
                </span>{' '}
                <span className="mono">{status.host}:{status.port}</span>
                {' '}from <span className="mono">{status.fromAddress}</span>
                {' '}· emails {status.minSeverity}+ · configured by {status.configuredBy}
              </>
            ) : (
              <span className="state state-pending">NOT CONFIGURED</span>
            )}
          </p>
        )}

        <div className="form-row">
          <label>SMTP host<input placeholder="mail.yourcompany.local" value={form.host}
            onChange={(e) => setForm({ ...form, host: e.target.value })} /></label>
          <label>Port<input type="number" value={form.port} style={{ width: 80 }}
            onChange={(e) => setForm({ ...form, port: e.target.value })} /></label>
          <label className="small"><input type="checkbox" checked={form.useSsl}
            onChange={(e) => setForm({ ...form, useSsl: e.target.checked })} /> SSL/TLS</label>
          <label>Username (optional)<input autoComplete="off" value={form.username}
            onChange={(e) => setForm({ ...form, username: e.target.value })} /></label>
          <label>Password<input type="password" autoComplete="off"
            placeholder={status?.configured ? '(unchanged)' : ''} value={form.password}
            onChange={(e) => setForm({ ...form, password: e.target.value })} /></label>
          <label>From address<input placeholder="alerts@yourcompany.com" value={form.fromAddress}
            onChange={(e) => setForm({ ...form, fromAddress: e.target.value })} /></label>
          <label>Email alerts at<select value={form.minSeverity}
            onChange={(e) => setForm({ ...form, minSeverity: e.target.value })}>
            <option value="Info">Info and above (everything)</option>
            <option value="Warning">Warning and above</option>
            <option value="Critical">Critical only</option>
          </select></label>
          <label className="small"><input type="checkbox" checked={form.enabled}
            onChange={(e) => setForm({ ...form, enabled: e.target.checked })} /> delivery enabled</label>
          <button onClick={save} disabled={busy || !form.host.trim() || !form.fromAddress.trim()}>Save</button>
        </div>

        <h3>Send a test message</h3>
        <div className="form-row">
          <label>To<input placeholder="you@yourcompany.com" value={testTo}
            onChange={(e) => setTestTo(e.target.value)} /></label>
          <button className="secondary" onClick={sendTest} disabled={busy || !testTo.includes('@')}>
            Send test
          </button>
        </div>
      </section>
    </div>
  )
}
