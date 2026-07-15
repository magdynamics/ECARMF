import { useCallback, useEffect, useState } from 'react'
import { api } from '../api'
import type { ActivityItem, PackageSummary, ScoreRecord } from '../types'
import { tenantConfig } from '../tenantConfig'
import { MaskedField } from './MaskedField'

interface HomeProps {
  tenant: string
  user: string
  go: (tab: string) => void
}

interface SearchResult {
  total: number
  items: ActivityItem[]
}

/// The guided front door: where to start, what's input, what's output,
/// with live status so each step shows whether it's already done.
export function Home({ tenant, user, go }: HomeProps) {
  const [packages, setPackages] = useState<PackageSummary[] | null>(null)
  const [records, setRecords] = useState<SearchResult | null>(null)
  const [scores, setScores] = useState<ScoreRecord[] | null>(null)
  const [libraryCount, setLibraryCount] = useState<number | null>(null)
  const [benchmarkCount, setBenchmarkCount] = useState<number | null>(null)
  const [integrationCount, setIntegrationCount] = useState<number | null>(null)
  const [agentCount, setAgentCount] = useState<number | null>(null)

  const refresh = useCallback(async () => {
    try { setPackages(await api.get<PackageSummary[]>('/api/packages')) } catch { setPackages(null) }
    try { setRecords(await api.get<SearchResult>('/api/records/search?pageSize=1')) } catch { setRecords(null) }
    try { setScores(await api.get<ScoreRecord[]>('/api/scores?limit=100')) } catch { setScores(null) }
    try { setLibraryCount((await api.get<unknown[]>('/api/library?limit=100')).length) } catch { setLibraryCount(null) }
    try { setBenchmarkCount((await api.get<unknown[]>('/api/benchmarks')).length) } catch { setBenchmarkCount(null) }
    try { setIntegrationCount((await api.get<unknown[]>('/api/integrations')).length) } catch { setIntegrationCount(null) }
    try { setAgentCount((await api.get<unknown[]>('/api/agents')).length) } catch { setAgentCount(null) }
  }, [])

  useEffect(() => { void refresh() }, [refresh, tenant, user])

  const activePackages = packages?.filter((p) => p.state === 'Active').length ?? 0
  const recordCount = records?.total ?? 0
  const scoreCount = scores?.length ?? 0

  const Step = (props: {
    n: number
    title: string
    kind: 'setup' | 'input' | 'output'
    done: boolean
    status: string
    action: string
    tab: string
    children: string
  }) => (
    <div className={`step ${props.done ? 'step-done' : ''}`}>
      <div className="step-head">
        <span className="step-number">{props.done ? '✓' : props.n}</span>
        <strong>{props.title}</strong>
        <span className={`state state-${props.kind === 'input' ? 'staged' : props.kind === 'output' ? 'approved' : 'flagged'}`}>
          {props.kind === 'setup' ? 'SETUP' : props.kind.toUpperCase()}
        </span>
      </div>
      <p className="muted small">{props.children}</p>
      <div className="step-foot">
        <span className="small">{props.status}</span>
        <button onClick={() => go(props.tab)}>{props.action}</button>
      </div>
    </div>
  )

  return (
    <div>
      <section className="panel">
        <h2>Welcome — how this platform works</h2>
        <p className="muted">
          Data goes <strong>in</strong> — typed records, raw source payloads, uploaded documents
          (PDFs, statements, tax returns), or feeds from your business applications. The kernel
          processes everything through the rules of your active Knowledge Packages and checks your
          benchmarks. The results come <strong>out</strong> — explained outcomes, scores and KPIs,
          alerts, capital recommendations, and answers from specialized AI agents — with every
          original upload preserved in the Library. Follow the steps below; each shows its live
          status for tenant <strong>{tenant}</strong>.
        </p>
      </section>

      {tenantConfig(tenant).phi && (
        <section className="panel" style={{ borderLeft: '3px solid var(--tenant-accent)' }}>
          <h2>Regulated data — HIPAA / PHI</h2>
          <p className="muted">
            This tenant handles protected health information. PHI-touching fields are
            masked by default and carry a <span className="phi-badge">PHI</span> badge;
            revealing one is an explicit action that is audit-logged server-side, so who
            saw what and when is always attributable. Example:
          </p>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
            <div>
              <span className="muted small" style={{ marginRight: '0.5rem' }}>Member ID</span>
              <MaskedField value="MBR-4471-882-0093" fieldKey="member.memberId" subjectRef="CLM-2026-0006" screen="home" />
            </div>
            <div>
              <span className="muted small" style={{ marginRight: '0.5rem' }}>Diagnosis (ICD-10)</span>
              <MaskedField value="E11.9 — Type 2 diabetes mellitus without complications" fieldKey="claim.diagnosis" subjectRef="CLM-2026-0006" screen="home" />
            </div>
          </div>
        </section>
      )}

      <Step n={1} title="Activate Knowledge Packages" kind="setup" tab="packages"
        done={activePackages > 0}
        status={packages === null ? 'Sign in as the Administrator to manage packages.' : `${activePackages} active package(s).`}
        action="Open Package Inspector">
        Packages define what the system knows: entities, rules, events, scoring, KPI frameworks,
        workflows, and AI agents. Regulatory content (COSO, GAAP, Reg D, AML, IRS reference rates)
        ships this way. Nothing is processed until at least one package is active.
      </Step>

      <Step n={2} title="Connect your applications & set expectations" kind="setup" tab="integrations"
        done={(integrationCount ?? 0) > 0 || (benchmarkCount ?? 0) > 0}
        status={`${integrationCount ?? 0} integration(s) · ${benchmarkCount ?? 0} benchmark(s) configured.`}
        action="Open Integrations">
        Optional but powerful: register feeds from accounting, POS, billing, or property systems
        (Integrations), and state your expectations — "GP% ≥ 25%", "no movement above 10k" — with
        alarm severity and routing (Benchmarks). The AI Backend tab holds this tenant's own AI
        credential.
      </Step>

      <Step n={3} title="Bring data in" kind="input" tab="dataentry"
        done={recordCount > 0}
        status={`${recordCount} record(s) received so far.`}
        action="Open Data Entry">
        The INPUT screen, three ways in: submit a typed record, paste a raw source payload through
        a connector, or upload a document (a PDF tax return, an invoice, an email) and let the
        extraction agent turn it into records.
      </Step>

      <Step n={4} title="See what the system decided" kind="output" tab="activity"
        done={recordCount > 0}
        status={`${recordCount} record(s) — filter by type, outcome, or text.`}
        action="Open Record Activity">
        Every record with its outcome, the exact rule and package version that decided it, and the
        reason — filterable and paged for thousands of records. Flagged items are released here via
        dual approval.
      </Step>

      <Step n={5} title="Review the intelligence" kind="output" tab="dashboard"
        done={scoreCount > 0}
        status={`${scoreCount} score(s) computed (KPIs, ratings, trust, accuracy…).`}
        action="Open Dashboard">
        Configurable widgets: score averages, outcome breakdown, OKR attainment, deviation and
        benchmark alarms, integration health, and your open task inbox.
      </Step>

      <Step n={6} title="Trace the evidence" kind="output" tab="library"
        done={(libraryCount ?? 0) > 0}
        status={`${libraryCount ?? 0} document(s) archived.`}
        action="Open Library">
        Every upload is archived verbatim and indexed — source, uploader, hash, and the records it
        produced. The original evidence behind any decision is always one search away.
      </Step>

      <Step n={7} title="Decide capital & consult the agents" kind="output" tab="advisor"
        done={(agentCount ?? 0) > 0}
        status={`${agentCount ?? 0} specialized agent(s) available · Executive Advisor always on.`}
        action="Open AI Advisor">
        The Executive Advisor writes trust-tracked briefs; package-shipped specialists (like the
        IRS Corporate Tax Guide) answer questions grounded in your data. Capital allocations wait
        under Allocations for the Owner to approve, modify, or reject.
      </Step>
    </div>
  )
}
