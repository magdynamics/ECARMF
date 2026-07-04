# Knowledge Packages

Sample Knowledge Package manifests for the ECARMF Platform Kernel. A package
contributes entities, events, rules, and capabilities as pure metadata — the
kernel executes declarations; it never contains package code.

## Loading a package

```bash
# 1. Upload (stage) the manifest
curl -X POST http://localhost:5099/api/packages \
  -H "Content-Type: application/json" \
  -d @packages/treasury-controls-v1.json

# 2. Activate it
curl -X POST http://localhost:5099/api/packages/ecarmf.treasury-controls/1.0.0/activate

# 3. Submit a transaction that trips the control
curl -X POST http://localhost:5099/api/transactions \
  -H "Content-Type: application/json" \
  -d '{"transactionType":"withdrawal","submittedBy":"treasurer@example.com","payload":{"ventureId":"V-001","amount":60000}}'
```

## Packages

| File | Package | What it proves |
|---|---|---|
| `treasury-controls-v1.json` | ecarmf.treasury-controls 1.0.0 | End-to-end pipeline: withdrawals over $50,000 are Flagged for dual approval (TREASURY-R-001); everything else is approved by default policy. |
