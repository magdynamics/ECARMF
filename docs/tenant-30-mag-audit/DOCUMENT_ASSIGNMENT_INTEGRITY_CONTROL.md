# Document Assignment Integrity Control

## Control objective

A document must be connected to the correct client, taxpayer, case, jurisdiction, return,
and tax period. Correcting an intake assignment must never alter the original evidence or
erase the prior assignment.

## Controlled correction

1. Select the document in **Document ownership and integrity**.
2. Select the correct client case.
3. Enter a specific correction reason.
4. Confirm the move.
5. The system verifies that the target case exists and does not already contain the same hash.
6. The system changes the logical case assignment in one database transaction.
7. It records the prior case, new case, document ID, SHA-256 hash, reason, operator, and time.
8. Existing OCR derivatives remain attached to the document.
9. Active analysis under the incorrect case is superseded and must be restarted under the
   correct case.

## Evidence preservation

The original bytes, filename, SHA-256 hash, upload record, and local storage path remain
unchanged. This is a custody correction, not a replacement or deletion. Assignment history
is append-only and visible to staff from the user interface.

## Required future production controls

- Role-based permission for reassignment
- Step-up authentication for cross-client moves
- Two-person approval for restricted or privileged documents
- Client and sponsor access recalculation immediately after a move
- Legal hold and retention-policy checks
- Notification to the case owner and privacy officer where required
- Independent evidence-object storage so physical paths do not encode case ownership
- Automated verification that search indexes, permissions, tasks, and downstream analyses
  reflect the corrected case
