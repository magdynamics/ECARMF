# ECARMF — Runbook: turn on AI + go live

Two independent switches take the platform from "built and running in test mode
on the `:8080` process" to "AI-powered production on the durable `:5099`
service." They can be done in either order; do **AI first** if you want to test
it before locking down.

Current state (as shipped):

| | State | What it means |
|---|---|---|
| AI backend | **off** — no key, no local model, 0 tenants configured | agents/advisor/onboarding-AI use the deterministic fallback |
| Runtime | `:8080` **process**, `AllowHeaderIdentity: true` | open test mode — anyone can act as any non-regulated tenant |
| Durable service | `ECARMF` **Stopped**, `:5099` down | not yet the production instance |
| Shortcut | → `http://localhost:8080/?tenant=platform` | points at the test process |

The `:8080` process and the `:5099` service read the **same** `C:\ECARMF\app`
files and the **same** database, so keys and config carry across both.

---

## Part A — Turn on AI

Pick **one** backend. Nothing here requires code changes.

### Option A1 — Local model (no key, nothing leaves the machine) — recommended for a private trial
1. Install **Ollama** from <https://ollama.com>, then pull a model:
   ```
   ollama pull llama3.1
   ```
   Ollama then serves an OpenAI-compatible API at `http://localhost:11434`.
2. Point a tenant at it — **UI**: open the app → pick a non-regulated tenant →
   **Setup → AI Backend** → provider **Local (on-prem)**, endpoint
   `http://localhost:11434`, model `llama3.1` → **Save**.
   *(Or API, as operator: `PUT /api/settings/ai` with
   `{"provider":"local","endpoint":"http://localhost:11434","model":"llama3.1"}`
   and the tenant's headers.)*
3. Test: go to **AI Agents**, pick an agent, ask a question → you should get a
   live, grounded answer (provenance `AIGenerated`, model `local:llama3.1`).

### Option A2 — Anthropic key (per tenant)
- **UI**: tenant → **Setup → AI Backend** → provider **Anthropic** → paste your
  API key (optionally a model, e.g. `claude-sonnet-5`) → **Save**. The key is
  stored hashed; only a hint is ever shown back.

### Option A3 — Anthropic key platform-wide (default for all tenants)
- Set an environment variable (or `Anthropic:ApiKey` in
  `C:\ECARMF\app\appsettings.json`) **before starting the service**:
  ```powershell
  [Environment]::SetEnvironmentVariable('ANTHROPIC_API_KEY','sk-ant-...','Machine')
  ```
  Every tenant without its own backend then uses this as the platform default.

> The platform never lets an agent see beyond its declared context, and every
> consult is advisory-only — turning AI on does not change those guarantees.

---

## Part B — Go live on the durable `:5099` service

Do the steps **in this order** — with header identity off, every request needs
an access key, so issue keys *before* you lock down or you'll be locked out.

### B1. Issue at least one operator access key (while still in test mode)
Seeded users have **no** key by default. While `:8080` is up (header identity
on), get an operator key — **API** (operator):
```
POST /api/platform/tenants/platform/users/owner@platform/rotate-key
```
The response contains `accessKey` **once** — save it in your password manager.
Issue client keys the same way from the **Clients** screen (provision a user →
key shows once).

### B2. Run the go-live helper (elevated)
Open **PowerShell as Administrator** and run:
```powershell
cd "C:\Users\moabughoush\Desktop\ecarmf test"
.\deploy\go-live.ps1 -LockDown -RepointShortcut
```
The script: stops the `:8080` test process, sets `AllowHeaderIdentity=false`,
starts the `ECARMF` service (`:5099`), waits for it to answer, and repoints the
desktop shortcut to `:5099`. (Omit `-LockDown` to go live but stay in open mode;
omit `-RepointShortcut` to leave the shortcut alone.)

### B3. Verify
- Open the desktop shortcut → it should load `http://localhost:5099` and show
  the **Sign in** screen (key required).
- Sign in with the operator key from B1.

### Rolling back to test mode
```powershell
.\deploy\go-live.ps1 -Unlock          # sets AllowHeaderIdentity=true, restarts service
```
or `Stop-Service ECARMF` and start the `:8080` process again.

---

## Part C — Hardening (one-time setup, elevated)

### C1. Config overlay (how settings survive deploys)
Machine-specific settings live in `C:\ECARMF\app\appsettings.Production.json` — connection string,
`Security:AllowHeaderIdentity`, and (optionally) HTTPS endpoints. ASP.NET loads it automatically
(the default environment is Production), and every deploy mirror excludes it
(`robocopy ... /XF appsettings.Production.json`), so deploys can never clobber it again.
`go-live.ps1 -LockDown/-Unlock` edits this overlay, not the base file.

### C2. Enable HTTPS (pure config — no code change)
Add a `Kestrel` section to `appsettings.Production.json`. **Important:** once `Kestrel:Endpoints`
exists it *replaces* `--urls`, so declare BOTH endpoints:
```json
"Kestrel": {
  "Endpoints": {
    "Http":  { "Url": "http://*:5099" },
    "Https": { "Url": "https://*:5443",
               "Certificate": { "Path": "C:\\ECARMF\\certs\\ecarmf.pfx", "Password": "<pfx password>" } }
  }
}
```
Internal/self-signed cert: `New-SelfSignedCertificate -DnsName <host> ...` + `Export-PfxCertificate`.
Public exposure: prefer a reverse proxy (IIS ARR / Caddy) terminating a real certificate.
Verified: with both endpoints configured, HTTP and HTTPS answer side by side; HSTS is emitted on
HTTPS for non-localhost hosts.

### C3. Backups (script verified: backup + restore drill both proven)
```powershell
.\deploy\register-backup-task.ps1        # elevated, once — nightly 02:00 as SYSTEM, keeps 14
.\deploy\backup-nightly.ps1              # manual backup any time (works unelevated)
.\deploy\backup-nightly.ps1 -VerifyRestore   # quarterly drill: restores newest .bak, checks, drops
```
Log: `C:\ECARMF\logs\backup.log`. Backups land in `C:\ECARMF\backups`.

### C4. Health monitoring
```powershell
.\deploy\register-health-probe.ps1       # elevated, once — probes /health every 5 min
```
Failure ⇒ Application event log **Error 1001** (source `ECARMF-Monitor`) + `C:\ECARMF\logs\health.log`;
recovery ⇒ Information 1000. Attach an email/toast to the event via Task Scheduler if desired.

### C5. Rate limiting (built in — nothing to configure)
Global: 300 requests / 30 s per client IP (429 + Retry-After beyond that). Credential routes
(key issuance/rotation, AI-key config) additionally capped at 10/min. Health probes are exempt.

## Quick reference

| Action | Command / location |
|---|---|
| Set AI (local) | Tenant → Setup → AI Backend → Local → `http://localhost:11434` |
| Set AI (Anthropic) | Tenant → Setup → AI Backend → Anthropic → paste key |
| Operator key | `POST /api/platform/tenants/platform/users/owner@platform/rotate-key` |
| Client keys | Clients screen → provision user (key shows once) |
| Go live | elevated `.\deploy\go-live.ps1 -LockDown -RepointShortcut` |
| Service status | `Get-Service ECARMF` |
| Back to test | `.\deploy\go-live.ps1 -Unlock` |
