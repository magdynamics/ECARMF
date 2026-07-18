# MAG Audit System — Master Product Narrative and Build Baseline

## 1. The product we are building

**MAG Audit** is a multi-jurisdiction tax examination readiness, audit defense, case management, and organizational intelligence platform for taxpayers, CPA professionals, attorneys, referral sponsors, and authorized staff.

MAG Audit is not merely a library of IRS documents, a tax-return risk score, or a client upload portal. It is the governed operating system that brings all of those capabilities together around a taxpayer and a specific case.

The system is designed to:

1. onboard and maintain taxpayers, clients, contacts, sponsors, staff, and authorizations;
2. open a separate governed case for each taxpayer, jurisdiction, tax type, form, and period under review;
3. collect, classify, protect, search, reconcile, and preserve tax returns and supporting evidence;
4. apply official-source knowledge about examination selection, procedures, techniques, taxpayer rights, controversies, penalties, and resolution options;
5. analyze filed returns and supporting records without claiming access to confidential government selection formulas;
6. create explainable risk, exposure, documentation, controversy-readiness, and confidence assessments;
7. generate a customized professional audit program and coordinated work for staff and governed AI agents;
8. manage correspondence, deadlines, powers of attorney, document requests, contacts, escalation paths, findings, recommendations, and resolution;
9. keep the client informed and demonstrate the value delivered; and
10. preserve verified outcomes and approved learning so the organization becomes more capable over time.

The product promise is therefore:

> MAG Audit turns tax returns, supporting evidence, official examination knowledge, professional judgment, and controlled AI assistance into a transparent, defensible, end-to-end audit readiness and resolution process.

## 2. The correct system boundary

MAG Audit contains three major domains.

### Domain A — Shared MAG Audit platform

This is the common operating environment used for every jurisdiction. It owns:

- taxpayer and client profiles;
- engagements and cases;
- sponsors and referral relationships;
- consent and access permissions;
- staff and governed AI-agent assignments;
- secure document intake and the evidence vault;
- case workspace, tasks, calendars, deadlines, contacts, and escalations;
- communications, e-signature, authorizations, and client transparency;
- findings, recommendations, actions, resolutions, outcomes, deliverables, and value reporting;
- security, audit trails, retention, governance, and organizational learning.

### Domain B — Jurisdiction audit engines

Each taxing authority has a separate governed module that supplies its own forms, law, procedures, techniques, rights, deadlines, remedies, and knowledge base.

The current federal module is:

- **US-IRS — IRS Audit Intelligence Knowledge Base and Audit Engine.**

The reserved next state module is:

- **US-IL-IDOR — Illinois Department of Revenue Audit Engine.** Its platform space and jurisdiction gate exist, but its substantive knowledge base has intentionally not yet been populated.

The same design can later support other states and agencies without mixing their authority with IRS authority.

### Domain C — Intelligence and professional-control layer

This layer coordinates rules, analytics, AI agents, source citations, confidence, review gates, change impact, and approved learning. It assists qualified professionals; it does not make unreviewed legal conclusions or autonomous external commitments.

## 3. The central data model

The center of MAG Audit is not a PDF. It is the **case**.

The relationship is:

```text
Sponsor / Referral Source
          |
          v
Client / Taxpayer ---- Authorized representatives and contacts
          |
          v
Case = jurisdiction + tax type + return/form + tax period + service scope
          |
          +---- Tax return package and extracted facts
          +---- Supporting documents and evidence
          +---- Government notices, IDRs, correspondence, and workpapers
          +---- Staff and AI-agent work
          +---- Issues, risks, findings, recommendations, and actions
          +---- Deadlines, contacts, escalations, and rights
          +---- Resolution, verified outcome, deliverables, and value
```

A taxpayer may have many cases. A case must never blend periods, jurisdictions, or authorizations merely because they belong to the same client.

## 4. Complete end-to-end workflow

### Stage 1 — Referral, sponsor, and relationship intake

A sponsor may be a CPA, accountant, attorney, financial professional, or other third party who introduces a taxpayer. MAG Audit records the sponsor, referral source, relationship, and referred cases.

Referral does not automatically provide access. Sponsor access is deny-by-default and requires explicit client consent. Permission can be limited by case, taxpayer, year, action, artifact, and time. The client may authorize no access, limited status visibility, document participation, or broader collaboration. Restricted legal, privilege-sensitive, internal deliberation, and other protected materials remain separately controlled.

### Stage 2 — Client onboarding and profile

The system welcomes the client, explains the service, confirms identity and contact information, records entity and ownership information, defines communication preferences, presents the service agreement, captures required consent, and establishes the taxpayer profile.

The profile supports individuals, corporations, S corporations, partnerships, estates and trusts, exempt organizations, foreign filers, and other taxpayer types. It is designed to retain reusable taxpayer information while keeping every tax-period case separate.

### Stage 3 — Case creation and scope

A case is opened for a specific jurisdiction, return type, tax year or period, notice or examination status, and agreed service scope. Examples include a 2024 Form 1120-S readiness review, a 2022 IRS field examination, an IRS correspondence audit, or a future Illinois sales or income tax matter.

The case records material dates, statutes, response deadlines, assigned team, authority contacts, communications, authorizations, and escalation history.

### Stage 4 — Document request, upload, and evidence vault

The client receives a guided checklist based on taxpayer type, return, schedules, issues, and notices. Expected material may include:

- filed and amended returns;
- Forms W-2, 1099, K-1, 1098, and other information returns;
- general ledgers, trial balances, financial statements, and tax workpapers;
- bank, merchant, mortgage, brokerage, payroll, loan, and retirement statements;
- invoices, receipts, contracts, fixed-asset records, inventory records, and mileage logs;
- ownership, basis, capital, distribution, and related-party records;
- IRS notices, audit letters, Information Document Requests, transcripts, reports, workpapers, and correspondence;
- prior representative files, legal documents, and client explanations.

Files pass through validation, malware protection, quarantine where necessary, deduplication, hashing, metadata capture, classification, extraction/OCR, indexing, privilege and sensitivity labeling, and professional acceptance. Original evidence remains preserved; extracted facts point back to their source, page, field, and extraction method.

### Stage 5 — Return recognition and canonical tax model

MAG Audit identifies the return, version, tax year, entity, schedules, and available electronic-filing XML. It converts reported amounts into a version-aware canonical model while preserving provenance.

The current return corpus covers annual forms and instructions for 2018–2025, including Forms 1040, 1040-NR, 1041, 1065, 1120, 1120-F, 1120-S, 990, 990-EZ, 990-PF, and 990-T. Supporting schedule work currently includes Schedule C, E, F, K-1 packages, and selected Schedule M-3 packages.

Versioned line mapping is essential because the meaning and location of form lines can change from year to year. Strict mapping rejects unsupported year/form combinations instead of silently applying a wrong rule.

### Stage 6 — Completeness and reconciliation

The reconciliation engine compares the filed return with source records and related filings. Intended controls include:

- return-to-books and books-to-general-ledger reconciliation;
- gross receipts to bank deposits, merchant statements, point-of-sale records, and Forms 1099;
- payroll expense to Forms 941, W-2, and compensation records;
- sales, payroll, and other filings to income-tax reporting;
- K-1, ownership, basis, capital, loan, and distribution reconciliation;
- balance-sheet, retained-earnings, and roll-forward testing;
- schedule completeness and expected-document testing;
- current-to-prior-year and related-entity comparisons.

Every exception must distinguish missing evidence, extraction uncertainty, timing or classification differences, explainable reconciling items, and potential tax issues.

### Stage 7 — Jurisdiction knowledge and examination intelligence

The IRS engine combines several knowledge families:

1. Audit Technique Guides and industry knowledge.
2. Internal Revenue Manual examination selection, planning, examination, income-probe, penalty, workpaper, closing, and specialized procedures.
3. Annual forms, instructions, schedules, and line definitions.
4. IRS Statistics of Income and Data Book material for public benchmarks and examination context.
5. Public IRS guidance about audits, appeals, taxpayer rights, records, notices, and resolution.
6. Binding and persuasive authority that must ultimately include the Internal Revenue Code, Treasury Regulations, controlling cases, revenue rulings, revenue procedures, notices, and other current-law sources.
7. Specialized programs such as LB&I, exempt organizations, employment tax, partnerships, international, penalties, fraud, and other examination areas.

The system keeps guidance hierarchy visible. ATGs and the IRM describe administrative examination methods but do not replace controlling law.

### Stage 8 — Analytical and risk engines

MAG Audit does not promise to predict confidential IRS selection decisions. It produces separate, explainable professional assessments:

- public selection-indicator score;
- potential adjustment-exposure score;
- documentation-readiness score;
- controversy-readiness score;
- confidence and data-quality score;
- remediation urgency and case-priority classification.

Inputs can include return characteristics, public examination statistics, information-return mismatches, ratios and benchmarks, multiyear changes, cash intensity, related parties, accounting methods, missing documents, reconciliation exceptions, issue-specific authority, and professional overrides.

Scores must show their factors, sources, weights, uncertainty, exclusions, version, and reviewer. A high score directs attention; it does not establish wrongdoing or guarantee examination.

### Stage 9 — Chief Audit Officer and customized audit program

The Chief Audit Officer engine assembles the case picture and recommends a customized audit-readiness or defense program. It selects applicable issues, techniques, evidence requirements, interview topics, reconciliations, rights controls, specialist needs, and review steps.

The audit program becomes accountable work assigned to staff and governed AI agents. Completion requires evidence and review—not merely checking a box.

### Stage 10 — Staff and AI case operations

The workspace brings together:

- case dashboard and current posture;
- taxpayer, sponsor, representative, and authority contacts;
- staff roles, assignments, workload, and approvals;
- AI-agent role, permissions, inputs, outputs, confidence, and required human review;
- document requests and evidence status;
- audit-program steps, tasks, dependencies, and deadlines;
- calls, meetings, notes, correspondence, and escalation history;
- findings, recommendations, actions, deliverables, and client updates.

AI agents may assist with intake, classification, extraction, reconciliation, research, audit-program drafting, communication drafting, deadline monitoring, issue analysis, rights checking, and outcome summaries. They may not independently sign, submit, call an authority, waive a right, disclose restricted information, or make a final professional/legal conclusion.

### Stage 11 — Authorizations, communications, rights, and controversy

The system supports service agreements, consent, e-signature journeys, and preparation and tracking of Form 2848 or other required authorizations. External actions remain gated until scope, authority, recipient, attachments, deadlines, and human approval are confirmed.

The rights and controversy layer is designed to manage:

- taxpayer rights and procedural protections;
- IRS notices and Information Document Requests;
- response strategy, scope, relevance, burden, privilege, and production logs;
- statutes, extensions, claims, audit reconsideration, Appeals, mediation, collection and litigation pathways where applicable;
- contacts with examiners, managers, specialist units, Appeals, and other escalation channels;
- penalty development and defenses;
- administrative record and workpaper preservation.

This layer requires qualified CPA and legal review according to the issue and forum.

### Stage 12 — Findings, recommendations, actions, and remediation

A potential issue becomes a finding only when facts, evidence, authority, financial impact, confidence, and review state are recorded. Each recommendation must connect to a finding and define responsibility, due date, expected benefit, risk, client decision, and approval requirements.

Actions may include obtaining evidence, correcting books, preparing a reconciliation, responding to an IDR, making a procedural request, amending a return, changing a control, or taking no action with documented rationale. External actions require a separate approval gate.

### Stage 13 — Resolution, outcome, and value

MAG Audit distinguishes:

- an action that was performed;
- a resolution that was reached;
- an outcome that was independently verified; and
- value that can be responsibly attributed and communicated.

The system produces audience-specific deliverables for the client, sponsor where authorized, engagement team, firm leadership, and future reviewers. Value may include tax exposure reduced, penalties avoided, time saved, records improved, uncertainty resolved, deadlines protected, and controls strengthened. Claims must remain evidence-based.

### Stage 14 — Controlled learning and organizational memory

Closed cases do not automatically rewrite the engine. Proposed lessons retain source, context, outcome, reviewer, applicability, and approval status. Approved lessons may update playbooks, document checklists, rules, prompts, audit programs, training, and source-impact alerts. This creates a dynamic and evolving system without allowing uncontrolled self-learning to corrupt professional standards.

## 5. Knowledge-base architecture

The knowledge system is composed of connected libraries:

1. Source and immutable version registry.
2. Guide, chapter, section, page, exhibit, and glossary library.
3. Legal and administrative authority library.
4. Taxpayer, return, industry, and jurisdiction segmentation library.
5. Tax issue and risk-indicator library.
6. Examination-technique and audit-procedure library.
7. Evidence and document-requirement library.
8. Interview-question and questionnaire library.
9. IDR, correspondence, notice, and workpaper library.
10. Form, instruction, schedule, and versioned-line library.
11. Public statistics, ratios, and benchmarking library.
12. Taxpayer-rights, penalty, controversy, and remedy library.
13. Audit-program, remediation, deliverable, and outcome library.
14. Approved organizational-learning library.

Every usable knowledge object should ultimately identify its official source, version, retrieval date, effective period, page or section, legal-reliance level, current-law state, applicable jurisdiction, applicable taxpayer and return types, reviewer, and affected engine rules.

## 6. Source corpus currently preserved

The repository currently preserves approximately **205 MB across 382 source files**:

- 325 PDF files;
- 44 preserved HTML pages;
- 11 spreadsheets; and
- 2 source-readme records.

At least **9,172 pages are presently confirmed** in the counted corpora:

- 2,482 pages in the mainstream ATG-index corpus;
- 5,620 pages in the annual return forms and instructions manifest; and
- 1,070 pages in the supporting schedule manifest.

Additional publications and sources are present but are not included in that minimum page total.

The mainstream ATG database currently contains:

- 43 registered sources;
- 2,604 searchable sections;
- 2,396 machine-extracted authority candidates;
- 5,755 section-to-authority links; and
- 1,979 machine-extracted technique candidates.

The word **candidate** is important. Machine extraction creates a review queue; it does not create a legally current, professionally approved rule.

## 7. Software and data components already built

The present repository contains:

- 15 ordered database migrations covering the foundation through outcome and value;
- 25 Python modules for ingestion, extraction, intake, canonical modeling, line mapping, reconciliation, portfolio analysis, penalties, jurisdiction controls, sponsors, client engagement, case operations, outcomes, and organizational control;
- 20 automated test areas with 77 tests passing at the latest integrated check;
- demonstration case packages for intake, returns, sponsors, jurisdiction gating, client engagement, case operations, outcomes, and portfolio assessment;
- a populated searchable SQLite knowledge database;
- a local MAG Audit visual demonstration and desktop launcher;
- architecture, governance, workflow, and component design documents;
- source manifests and repeatable source-download scripts.

The major implemented design components are:

1. IRS source registry, extraction, citation candidates, full-text search, and technique candidates.
2. Rules-based audit-readiness and portfolio assessment foundation.
3. Return intake and canonical fact model.
4. Versioned tax-form line mapping.
5. Return reconciliation and completeness controls.
6. Chief Audit Officer analytical package.
7. Case workflow and production pipeline.
8. Organizational intelligence control plane.
9. Client upload and completeness portal controls.
10. Sponsor referral, consent, scoped access, and safeguards.
11. Multi-jurisdiction registry with an IDOR placeholder.
12. Client transparency, service-agreement, authorization, and AI engagement controls.
13. Staff and AI case operations workspace.
14. Finding-to-outcome, deliverable, value, and learning chain.
15. Navigable local demonstration interface.

## 8. Honest maturity assessment

The project has a substantial source corpus, a detailed data model, working analytical modules, migrations, tests, and a navigable demonstration. It is not yet one production application in which every screen persists data and invokes every engine.

### Working foundation

- official IRS source files are preserved locally;
- source hashes, manifests, extraction, and a searchable ATG SQLite database exist;
- core engines can be exercised through Python and demonstration JSON packages;
- database expansions and control logic exist;
- automated tests validate the implemented module behavior;
- the local application demonstrates the intended user journeys and safeguards.

### Designed or partially implemented

- complete client/taxpayer master database;
- persistent case lifecycle across the visual application;
- secure production document vault, OCR, and classification services;
- end-to-end return ingestion and reconciliation from real client files;
- live knowledge retrieval inside each case;
- production audit scoring and customized audit-program orchestration;
- staff identity, role, workflow, notifications, and approvals;
- sponsor and client portals backed by production authentication;
- e-signature, communications, transcript, and external service integrations;
- production dashboards, deliverables, outcome verification, and learning approvals.

### Corpus and professional-review gaps

- the complete IRM Part 4 and specialized IRM corpus;
- exempt-organization Technical Guides and examination materials;
- comprehensive LB&I, employment tax, partnership, international, estate/gift, excise, penalty, fraud, Appeals, litigation, and Chief Counsel materials;
- complete controlling-law linkage and current-law validation;
- missing supporting-schedule downloads identified by the manifest;
- SOI corporation reports after the currently preserved 2020–2022 files;
- substantive Illinois IDOR and other state knowledge modules;
- professional promotion of machine-extracted candidates through technical and legal-current review.

## 9. The integrated MAG Audit architecture

```text
Experience layer
  Client portal | Sponsor portal | Staff workspace | Leadership dashboard
                              |
Case and workflow layer
  Profiles | Cases | Evidence | Tasks | Deadlines | Communications | Outcomes
                              |
Shared intelligence layer
  Intake | OCR/extraction | Canonical facts | Reconciliation | Risk | Audit program
                              |
Jurisdiction layer
  IRS engine | IDOR engine | Future state and agency engines
                              |
Knowledge layer
  Sources | Authorities | Techniques | Forms | Rights | Statistics | Playbooks
                              |
Governance layer
  Identity | Consent | Permissions | Provenance | Review | Audit trail | Retention
```

## 10. The first complete working release

The next objective should be a single **MAG Audit Integrated Core** release that proves the whole system through one realistic but synthetic case.

The acceptance journey is:

1. Create a sponsor and a client.
2. Record consent and sponsor visibility.
3. Create a taxpayer profile.
4. Open a 2024 Form 1120-S IRS case.
5. Assign staff and governed AI-agent roles.
6. Upload a return, K-1 package, trial balance, bank support, payroll support, and IRS notice.
7. Preserve and classify the evidence and extract traceable facts.
8. Reconcile the return to supporting records and identify explainable exceptions.
9. Search the IRS knowledge base from the case with source citations.
10. Calculate separate explainable risk, exposure, readiness, controversy, and confidence scores.
11. Generate and approve a customized audit program.
12. Prepare a client request, Form 2848 workflow, deadlines, and a controlled IRS response package.
13. Track findings, recommendations, actions, and client updates.
14. Record a resolution, independently verify the outcome, issue deliverables, and close the case.
15. Propose a learning record for professional approval.

This vertical release must use a persistent database and real engine calls from the interface. It becomes the proof that the platform is one operational system rather than a collection of separate components.

## 11. Requirements-accountability standard

Every requested capability must be placed in a traceability register with:

- requirement identifier and narrative;
- product domain and jurisdiction;
- user role and workflow stage;
- database tables and migrations;
- service or engine module;
- interface screen;
- source and authority dependencies;
- security and approval requirements;
- automated and professional-validation tests;
- maturity: proposed, designed, coded, integrated, validated, production-ready, or deferred;
- owner, priority, dependency, and next action.

No future capability should be described as “built” merely because a screen, schema, or narrative exists. The maturity state must make clear whether it is visible, coded, connected, tested, professionally validated, and production ready.

## 12. Product principles that cannot be compromised

1. Official-source traceability and version control.
2. Clear separation of controlling law, administrative guidance, statistics, professional interpretation, and machine output.
3. Case-, period-, taxpayer-, action-, and artifact-scoped authorization.
4. Deny-by-default access and special protection for sensitive or privileged material.
5. Human approval for professional conclusions and all external actions.
6. Explainable scores with uncertainty; no claim to reproduce confidential selection systems.
7. Preservation of original evidence and provenance for every extracted fact.
8. Separate jurisdiction modules; no silent substitution of IRS logic for state law.
9. Transparent client communication and evidence-based value reporting.
10. Controlled learning with review, rollback, and impact analysis.

## 13. Final understanding

MAG Audit is one integrated system with a shared client-and-case operating platform, multiple jurisdiction-specific audit engines, authoritative knowledge bases containing thousands of pages, analytical and workflow engines, governed staff and AI collaboration, secure evidence management, professional approval gates, and outcome-focused client service.

The work completed so far is the foundation and a collection of functioning building blocks. The immediate responsibility is to connect those blocks into the first complete case journey, measure every capability against the requirements register, and expand the official knowledge corpus and professional review in parallel with integration.
