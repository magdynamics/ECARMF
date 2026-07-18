# Knowledge Governance

## Source hierarchy

Knowledge records must label their authority class. Suggested classes are controlling statute, regulation, controlling case, published administrative guidance, internal procedure, examination aid, form/instruction, and educational webpage.

## Required provenance

No extracted knowledge object may be promoted beyond `machine_extracted` without:

- an official source URL;
- a retrieved source artifact and SHA-256 hash;
- publication or revision date when available;
- precise page, section, or paragraph location;
- reviewer identity and review date;
- a current-law check appropriate to the issue.

## Professional review

Risk scores indicate documentation and examination-readiness concerns, not the probability of audit selection. Findings must be reviewed by a qualified CPA, attorney, or other authorized tax professional before client delivery.

## Change control

Never overwrite a source artifact. Register a new `source_version`, compare it with its predecessor, identify affected knowledge objects and rules, and retain historical assessments with their original engine version.
