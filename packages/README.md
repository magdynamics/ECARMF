# Knowledge Packages

Sample Knowledge Package manifests for the ECARMF Platform Kernel. A package
contributes entities, events, rules, capabilities, and score emissions as
pure metadata — the kernel executes declarations; it never contains package
code. Package versions are never overwritten: a changed manifest is a new
version (this is also how AI-learning threshold/weight updates ship).

## Loading a package

```bash
TENANT='X-Tenant-Id: tenant-alpha'

curl -X POST http://localhost:5099/api/packages -H "$TENANT" \
  -H "Content-Type: application/json" -d @packages/treasury-controls-v1.json
curl -X POST -H "$TENANT" \
  http://localhost:5099/api/packages/ecarmf.treasury-controls/1.1.0/activate
```

## Packages

| File | Package | What it proves |
|---|---|---|
| `treasury-controls-v1.json` | ecarmf.treasury-controls 1.1.0 | A simple control: withdrawals over $50,000 are Flagged for dual approval (TREASURY-R-001); everything else approved by default policy. 1.1.0 migrated the events to the generic record model (RecordReceived / Approved / Rejected / Flagged) — 1.0.0 remains in history, never overwritten. |
| `flywheel-opportunity-evaluation-v1.json` | ecarmf.flywheel-opportunity-evaluation 1.0.0 | The full flywheel on the same kernel mechanism: scoring-only rules emit DataConfidence / RiskScore / Valuation / Compliance / AssetReadiness ScoreRecords, decision rules produce Accept / Hold / Escalate / AuditFurther with reasoning, and follow-up rules learn Trust and ControlEffectiveness from outcomes. Depends on treasury-controls (RecordReceived). |
