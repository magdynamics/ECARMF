# Sponsor, Referral, and Consent-Based Access

## Control objective

A sponsor or referral partner may introduce a prospective client without receiving
any access to the client's file. Referral attribution and client-file authorization
are separate records and separate decisions. The permanent default is deny.

Access is permitted only when all of the following are valid at the time of access:

```text
active sponsor + matching referral + signed client consent + scoped access grant
+ firm approval + matching taxpayer/case/year/action + unexpired authorization
+ artifact release when restricted = allow
```

Failure of any required control produces a deny decision and a reason-coded access
event. Consent revocation immediately prevents future access but preserves the
immutable authorization and access history.

## Roles

A sponsor can be a referral source, document provider, collaborating professional,
return preparer, authorized representative, billing sponsor, or information-only
contact. A role is descriptive; it never grants a permission.

Supported granular permissions are:

- `view_status`
- `upload_documents`
- `view_selected_documents`
- `secure_messages`
- `view_selected_findings`
- `receive_released_report`
- `attend_meetings`
- `view_billing`

There is intentionally no global `full_access` permission. Collaboration is assembled
from minimum-necessary permissions limited by taxpayer, matter, tax year, document
category, selected document, effective date, and expiration date.

## Restricted material

Client consent does not force the firm to disclose privileged communications,
attorney work product, internal workpapers, draft conclusions, quality-review notes,
fraud assessments, or another taxpayer's information. These classes require an
individual `sponsor_artifact_release` approved by the firm.

## Client experience

The consent screen presents three clear choices:

1. Referral only; no file access.
2. Limited participation; the client selects allowed activities.
3. Case collaboration; the client selects the taxpayer, case, years, activities,
   and duration. The firm may narrow the scope before approval.

The client can review active sponsor grants and revoke them. The interface explains
that revocation prevents future system access but cannot retrieve copies legitimately
downloaded before revocation.

## Staff workflow

1. Register and verify the sponsor.
2. Record the referral without granting access.
3. Complete conflict and independence checks.
4. Capture versioned, signed client consent.
5. Create a minimum-necessary grant with a mandatory expiration date.
6. Obtain firm approval.
7. Log every allowed and denied action.
8. Require explicit release for restricted artifacts.
9. Periodically recertify access and close or revoke it when the matter ends.

## API surface

```text
POST   /sponsors
POST   /referrals
POST   /referrals/{referral}/consents
POST   /matters/{matter}/sponsor-access-grants
POST   /sponsor-access/evaluate
POST   /sponsor-access-grants/{grant}/artifact-releases
POST   /sponsor-access-grants/{grant}/revoke
GET    /clients/{client}/sponsor-access
GET    /matters/{matter}/sponsor-access-events
```

Every protected sponsor request must pass through the same server-side access
decision service. User-interface hiding is not an authorization control.

## Additional recommended safeguards

- Require MFA for all sponsor accounts and step-up verification for downloads.
- Notify the client when a grant is created, materially changed, or used for export.
- Apply download watermarking and recipient labels to released reports.
- Prohibit bulk export unless separately approved.
- Recertify long-running access at least every 90 days.
- Terminate grants automatically when the engagement closes or consent expires.
- Keep sponsor compensation, credential, conflict, and independence reviews outside
  ordinary case-document permissions.
- Provide a "view as sponsor" preview so staff can verify the exact disclosure set.

## Implemented operational safeguards

The authorization service also enforces the following operational rules:

- Sponsor MFA is mandatory for every authorized action.
- Downloads and exports require recent step-up verification.
- Bulk export requires a separately approved grant flag.
- Access must be recertified within 90 days.
- Closed, terminated, or archived matters deny future access.
- A security suspension overrides consent and every active grant.
- Allowed downloads produce a mandatory recipient/date watermark instruction and
  a client-notification requirement.
- The staff preview evaluates each proposed resource through the production access
  decision function; it does not approximate access from role names.
