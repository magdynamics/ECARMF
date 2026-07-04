import { useCallback, useEffect, useState } from 'react'
import { api } from '../api'
import type { ActivityItem, PackageSummary, ScoreRecord } from '../types'

interface HomeProps {
  tenant: string
  user: string
  go: (tab: string) => void
}

/// The guided front door: where to start, what's input, what's output,
/// with live status so each step shows whether it's already done.
export function Home({ tenant, user, go }: HomeProps) {
  const [packages, setPackages] = useState<PackageSummary[] | null>(null)
  const [records, setRecords] = useState<ActivityItem[] | null>(null)
  const [scores, setScores] = useState<ScoreRecord[] | null>(null)

  const refresh = useCallback(async () => {
    try { setPackages(await api.get<PackageSummary[]>('/api/packages')) } catch { setPackages(null) }
    try { setRecords(await api.get<ActivityItem[]>('/api/records?limit=50')) } catch { setRecords(null) }
    try { setScores(await api.get<ScoreRecord[]>('/api/scores?limit=100')) } catch { setScores(null) }
  }, [])

  useEffect(() => { void refresh() }, [refresh, tenant, user])

  const activePackages = packages?.filter((p) => p.state === 'Active').length ?? 0
  const recordCount = records?.length ?? 0
  const scoreCount = scores?.length ?? 0
  const decided = records?.filter((r) => r.outcomes.length > 0).length ?? 0

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
          Data goes <strong>in</strong> (records and opportunities you submit or ingest), the kernel
          processes it through the rules of your active Knowledge Packages, and the results come{' '}
          <strong>out</strong> (outcomes, scores, alerts, and capital recommendations). Follow the
          steps below in order — each one shows whether it's already done for tenant{' '}
          <strong>{tenant}</strong>.
        </p>
      </section>

      <Step n={1} title="Activate Knowledge Packages" kind="setup" tab="packages"
        done={activePackages > 0}
        status={packages === null ? 'Sign in as the Administrator to manage packages.' : `${activePackages} active package(s).`}
        action="Open Package Inspector">
        Packages define what the system knows: entities, rules, events, scoring, KPI frameworks.
        Nothing is processed until at least one package is active. (Requires the Administrator
        identity — use the "Acting as" switcher above.)
      </Step>

      <Step n={2} title="Bring data in" kind="input" tab="dataentry"
        done={recordCount > 0}
        status={`${recordCount} record(s) received so far.`}
        action="Open Data Entry">
        This is the INPUT screen: submit an opportunity or transaction manually, or paste a raw
        source payload (bank statement, journal entry, SiteView event) through a connector.
      </Step>

      <Step n={3} title="See what the system decided" kind="output" tab="activity"
        done={decided > 0}
        status={`${decided} of ${recordCount} record(s) have outcomes.`}
        action="Open Record Activity">
        This is the first OUTPUT screen: every record with its outcome (Approved / Flagged / Hold /
        Accept…), the exact rule that fired, and the reason. Flagged items can be released here via
        dual approval.
      </Step>

      <Step n={4} title="Review the intelligence" kind="output" tab="dashboard"
        done={scoreCount > 0}
        status={`${scoreCount} score(s) computed (confidence, readiness, trust, KPIs…).`}
        action="Open Dashboard">
        Aggregated OUTPUT: score averages, outcome breakdown, OKR attainment, trust movement, and
        deviation alerts.
      </Step>

      <Step n={5} title="Decide where capital goes" kind="output" tab="allocations"
        done={false}
        status="Generate a recommendation any time scores exist."
        action="Open Allocations">
        The platform's final answer: AI-generated allocation recommendations with reasoning and
        alternatives. Small/high-confidence ones auto-execute; the rest wait for the Owner to
        approve, modify, or reject.
      </Step>
    </div>
  )
}
