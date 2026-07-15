import { useMemo, useState } from 'react'

// System dictionary & help. Plain-language definitions of the platform's core
// concepts, grounded in how the kernel actually works, so operators and
// clients share one vocabulary. Terms cross-link to the screen where you see
// them. Content is static — this is documentation, not data.

interface Term {
  term: string
  short: string
  full: string
  tab?: string // screen where you see it
}
interface Group { name: string; blurb: string; terms: Term[] }

const GLOSSARY: Group[] = [
  {
    name: 'Tenancy',
    blurb: 'The platform serves many clients; everything belongs to exactly one of them.',
    terms: [
      { term: 'Tenant', short: 'One client of the platform.', tab: 'organization',
        full: 'A client organization. Every record, user, package, and setting is isolated by TenantId — no tenant can see another\'s data. A tenant is what you set up, feed data into, and monitor.' },
      { term: 'Operator', short: 'The platform admin who runs all tenants.',
        full: 'The platform administrator, working from the reserved "platform" tenant. Operators onboard clients, manage skills and billing, and can "act as" any tenant to see its data. Client tenants can never reach operator surfaces.' },
      { term: 'Act as', short: 'An operator viewing one client\'s data.',
        full: 'A platform operator temporarily viewing a specific client tenant. One operator credential can view every client this way; the client\'s data stays isolated.' },
      { term: 'Sensitivity tier / Posture', short: 'How strictly a tenant is governed.',
        full: 'Standard, Elevated, HighSensitivity, or Regulated. Higher tiers apply stricter defaults automatically — HighSensitivity+ refuses header-asserted identity (an access key is required), and Regulated preserves watched records instead of deleting them. Shown as the posture chip in the shell.' },
    ],
  },
  {
    name: 'Packaging & Skills',
    blurb: 'What a tenant can do is assembled from reusable, priced units.',
    terms: [
      { term: 'Knowledge Package', short: 'The executable unit of capability.', tab: 'packages',
        full: 'A JSON manifest declaring the entities, events, rules (controls), KPIs, agents, and knowledge assets a capability contributes. The kernel executes it as metadata — a package never contains code. Packages are loaded and activated per tenant.' },
      { term: 'Skill', short: 'A package presented commercially.', tab: 'skillslibrary',
        full: 'A knowledge package with a commercial wrapper — a tier and a price — that you turn on or off for a tenant. Example: "Autonomous Orchestration" (T9-041). Activating a skill installs its package (and dependencies).' },
      { term: 'Industry Package (bundle)', short: 'A set of skills for one industry.', tab: 'enroll',
        full: 'A captured combination of skills, benchmarks, and renewals for an industry (e.g. a Dental package or a Trading & Treasury package). Applied to a new tenant in one step during enrollment.' },
      { term: 'Essential vs À la carte', short: 'How a skill is billed.', tab: 'skillslibrary',
        full: 'Packaging set by the admin. Essential skills are bundled into the tenant\'s core/industry package and not billed separately. À la carte skills are metered — each active one adds a monthly line to the tenant\'s bill.' },
      { term: 'Tier', short: 'Core, Industry, or Add-on.', tab: 'skills',
        full: 'A skill\'s default grouping. Core = the spine every tenant gets (integrations, renewals, statements, foundations). Industry = industry-specific. Add-on = premium, metered capabilities.' },
    ],
  },
  {
    name: 'Controls & Governance',
    blurb: 'How the platform turns data into oversight.',
    terms: [
      { term: 'Control', short: 'A rule that judges records.', tab: 'controls',
        full: 'An executable rule that evaluates an incoming record against conditions and produces an outcome (e.g. Rejected or Flagged). Some packages also carry large reference control catalogs as knowledge assets.' },
      { term: 'Assertion', short: 'What a control protects.', tab: 'skillslibrary',
        full: 'The objective or risk a control defends — e.g. "prevents unauthorized or cross-tenant action", "protects liquidity", "protects privacy & PHI". A skill\'s value is expressed as which assertions, and how many controls, it covers.' },
      { term: 'Rule', short: 'The declaration behind a control.',
        full: 'The manifest form of a control: a trigger event (e.g. RecordReceived), conditions, an outcome, and a reason template. "Control" is the business word; "rule" is the technical one.' },
      { term: 'Knowledge Asset / Catalog', short: 'Reference documentation in a package.', tab: 'explore',
        full: 'Non-executable reference carried inside a package — a control catalog, policy, or boundary set. Often where a package\'s full "how it works" narrative lives (e.g. T9-041\'s 70-control catalog).' },
      { term: 'Risk register', short: 'Risks scored by severity × likelihood.', tab: 'risk',
        full: 'Risk-tagged KPIs plotted on a heatmap. Each risk carries a severity and likelihood; the index is their product.' },
    ],
  },
  {
    name: 'Data & Measurement',
    blurb: 'What flows in, and what the platform computes from it.',
    terms: [
      { term: 'Record', short: 'A submitted business item.', tab: 'dataentry',
        full: 'A unit of business data — a claim, a transaction, a statement line. The moment a record arrives it runs through the tenant\'s active controls.' },
      { term: 'Event', short: 'Something that happened.',
        full: 'A kernel signal (e.g. RecordReceived) that rules trigger on. The foundation package owns the shared events; dependent packages react to them.' },
      { term: 'Entity', short: 'A data type a package defines.',
        full: 'A declared record type with its attributes (e.g. PaymentObligation, LiquidityPosition). Packages contribute entities; records are instances of them.' },
      { term: 'KPI / Score', short: 'A computed metric.', tab: 'dashboard',
        full: 'A performance indicator computed from records over time. Scores carry a timestamp and optional metadata (used, for example, to place risks on the heatmap).' },
      { term: 'Benchmark', short: 'An expected value for a KPI.', tab: 'benchmarks',
        full: 'The threshold a KPI is measured against — a monitor flags when the metric crosses it.' },
      { term: 'Document', short: 'An ingested, indexed file.', tab: 'library',
        full: 'A source file (statement, contract) stored in the Library with a content hash and metadata, linked to the records extracted from it.' },
      { term: 'Renewal', short: 'A time-bound commitment.', tab: 'renewals',
        full: 'A compliance obligation or commitment with a due date and lead-time reminders (a license, a filing, a contract renewal).' },
    ],
  },
  {
    name: 'AI & Billing',
    blurb: 'The advisory layer and how usage becomes a bill.',
    terms: [
      { term: 'Agent', short: 'An advisory AI, grounded in one tenant.', tab: 'agents',
        full: 'A specialized AI advisor that can see only its declared context and is advisory-only — it recommends, humans decide. Needs the tenant\'s AI credential to answer.' },
      { term: 'AI Advisor', short: 'Cross-domain synthesizer.', tab: 'advisor',
        full: 'Assembles the outputs of multiple domains into an executive-facing view. Advisory-only.' },
      { term: 'Billing plan & Statement', short: 'Usage + skills become a bill.', tab: 'billing',
        full: 'A plan defines base fee and usage rates (per record, per AI call, per active user). Each monthly statement meters real usage and adds a line for every active à-la-carte skill.' },
    ],
  },
]

export function Glossary({ go }: { go: (tab: string) => void }) {
  const [q, setQ] = useState('')
  const f = q.trim().toLowerCase()

  const groups = useMemo(() => GLOSSARY.map((g) => ({
    ...g,
    terms: g.terms.filter((t) => !f
      || t.term.toLowerCase().includes(f) || t.short.toLowerCase().includes(f) || t.full.toLowerCase().includes(f)),
  })).filter((g) => g.terms.length > 0), [f])

  return (
    <div>
      <section className="panel">
        <h2>System dictionary &amp; help</h2>
        <p className="muted">
          Plain-language definitions of the platform&apos;s core concepts, so everyone shares one
          vocabulary. Each term links to where you see it in the app.
        </p>
        <input placeholder="Search the dictionary…" value={q} onChange={(e) => setQ(e.target.value)} style={{ width: '100%', maxWidth: '28rem' }} />
      </section>

      {groups.length === 0 ? (
        <section className="panel"><p className="muted">No terms match "{q}".</p></section>
      ) : groups.map((g) => (
        <section key={g.name} className="panel">
          <h3>{g.name}</h3>
          <p className="muted small">{g.blurb}</p>
          <dl className="glossary">
            {g.terms.map((t) => (
              <div key={t.term} className="gloss-item">
                <dt>
                  {t.term}
                  {t.tab && <button className="secondary small gloss-go" onClick={() => go(t.tab!)}>Open →</button>}
                </dt>
                <dd><strong className="gloss-short">{t.short}</strong> {t.full}</dd>
              </div>
            ))}
          </dl>
        </section>
      ))}
    </div>
  )
}
