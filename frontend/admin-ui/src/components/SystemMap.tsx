// System Map — the guided journey. The platform is powerful but wide (setup,
// input, output, AI, discovery, operator tools); a new user needs to see how
// the stages connect and where to go next. Every stage links straight into
// its screens, so this doubles as a launcher. Stages mirror the sidebar groups.
import { Icon } from './Icon'

interface Stage {
  n: number
  icon: string
  title: string
  what: string
  links: { label: string; tab: string }[]
  operator?: boolean
}

const STAGES: Stage[] = [
  {
    n: 1, icon: 'key', title: 'Sign in & pick a tenant',
    what: 'Everything you see belongs to one tenant. Operators pick a client to "act as"; each tenant\'s data is fully isolated.',
    links: [{ label: 'Start Here', tab: 'home' }],
  },
  {
    n: 2, icon: 'building', title: 'Set up what the tenant is & does',
    what: 'Define the organization, then load Knowledge Packages — the rules, controls, entities and KPIs that make the tenant work. Browse controls by domain.',
    links: [
      { label: 'Organization', tab: 'organization' },
      { label: 'Packages', tab: 'packages' },
      { label: 'Controls', tab: 'controls' },
    ],
  },
  {
    n: 3, icon: 'inbox-in', title: 'Feed data in',
    what: 'Submit records by hand or review incoming statements. Each record runs through the tenant\'s controls the moment it arrives.',
    links: [
      { label: 'Data Entry', tab: 'dataentry' },
      { label: 'Statement Review', tab: 'statements' },
    ],
  },
  {
    n: 4, icon: 'gauge', title: 'See the output',
    what: 'Watch what the controls produced — the record activity, the dashboard, the risk register, and formal reports.',
    links: [
      { label: 'Record Activity', tab: 'activity' },
      { label: 'Dashboard', tab: 'dashboard' },
      { label: 'Risk Register', tab: 'risk' },
      { label: 'Reports', tab: 'reports' },
    ],
  },
  {
    n: 5, icon: 'bot', title: 'Ask the AI',
    what: 'Consult the advisory agents grounded in this tenant\'s own data, or the cross-domain AI Advisor. Advisory-only — humans decide.',
    links: [
      { label: 'AI Advisor', tab: 'advisor' },
      { label: 'AI Agents', tab: 'agents' },
    ],
  },
  {
    n: 6, icon: 'compass', title: 'Discover everything',
    what: 'One searchable index of everything the tenant can do and knows — every control, KPI, agent, entity, event and knowledge asset across all its packages.',
    links: [{ label: 'Capability Explorer', tab: 'explore' }],
  },
  {
    n: 7, icon: 'sparkles', title: 'Grow the platform', operator: true,
    what: 'Operators: enroll new tenants in one flow, manage clients, and handle billing from the reserved operator console.',
    links: [
      { label: 'Enroll Tenant', tab: 'enroll' },
      { label: 'Clients', tab: 'clients' },
      { label: 'Billing', tab: 'billing' },
    ],
  },
]

export function SystemMap({ go }: { tenant: string; user: string; go: (tab: string) => void }) {
  return (
    <div>
      <section className="panel">
        <h2>System Map — get the most out of the platform</h2>
        <p className="muted">
          The platform runs your tenants end-to-end: set them up, feed data in, and let the
          controls and AI turn it into oversight. Follow the stages below — every step links
          straight into its screens. You can jump around freely; this is the recommended path.
        </p>
      </section>

      <div className="sysmap">
        {STAGES.map((s, i) => (
          <div key={s.n} style={{ display: 'contents' }}>
            <section className={`panel sysmap-stage ${s.operator ? 'sysmap-op' : ''}`}>
              <div className="sysmap-head">
                <span className="sysmap-num">{s.n}</span>
                <span className="sysmap-icon"><Icon name={s.icon} size={20} /></span>
                <h3>{s.title}</h3>
                {s.operator && <span className="posture-chip posture-elevated">Operator</span>}
              </div>
              <p className="muted small">{s.what}</p>
              <div className="sysmap-links">
                {s.links.map((l) => (
                  <button key={l.tab} className="secondary small" onClick={() => go(l.tab)}>{l.label} →</button>
                ))}
              </div>
            </section>
            {i < STAGES.length - 1 && <div className="sysmap-arrow" aria-hidden>↓</div>}
          </div>
        ))}
      </div>
    </div>
  )
}
