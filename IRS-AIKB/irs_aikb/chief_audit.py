"""Integrated, explainable Chief Audit Officer assessment engine."""

from __future__ import annotations

from dataclasses import asdict, dataclass
from typing import Any, Callable

ENGINE_VERSION = "0.3.0"


@dataclass(frozen=True)
class IssueFinding:
    issue_id: str
    dimension: str
    weight: int
    title: str
    observed_fact: str
    audit_technique: str
    evidence: tuple[str, ...]
    possible_explanations: tuple[str, ...]
    remediation: str
    authority_refs: tuple[str, ...]
    professional_review: str


@dataclass(frozen=True)
class IssueRule:
    issue_id: str
    dimension: str
    weight: int
    title: str
    predicate: Callable[[dict[str, Any]], bool]
    fact: Callable[[dict[str, Any]], str]
    audit_technique: str
    evidence: tuple[str, ...]
    explanations: tuple[str, ...]
    remediation: str
    authorities: tuple[str, ...]
    review: str = "CPA technical review"


def _num(data: dict[str, Any], key: str) -> float | None:
    value = data.get(key)
    if value is None or isinstance(value, bool): return None
    try: return float(value)
    except (TypeError, ValueError): return None


def _ratio(numerator: float | None, denominator: float | None) -> float | None:
    if numerator is None or denominator is None or denominator == 0: return None
    return numerator / denominator


def calculate_features(package: dict[str, Any]) -> dict[str, Any]:
    current = package.get("current_values", {})
    receipts = _num(current, "income.gross_receipts")
    gross_profit = _num(current, "income.gross_profit")
    total_income = _num(current, "income.total") or receipts
    deductions = _num(current, "expense.total")
    taxable = _num(current, "taxable_income.total")
    features: dict[str, Any] = {
        "gross_margin": _ratio(gross_profit, receipts),
        "deduction_ratio": _ratio(deductions, total_income),
        "taxable_margin": _ratio(taxable, total_income),
        "officer_comp_ratio": _ratio(_num(current, "expense.compensation_officers"), receipts),
        "other_deduction_ratio": _ratio(_num(current, "expense.other"), total_income),
        "related_receivable_asset_ratio": _ratio(
            _num(current, "balance.related_party_receivable"), _num(current, "balance.total_assets")),
    }
    external = package.get("external_values", {})
    features["bank_deposit_ratio"] = _ratio(_num(external, "bank_deposits"), receipts)
    features["information_return_ratio"] = _ratio(_num(external, "information_returns"), receipts)
    features["payroll_reconciliation_ratio"] = _ratio(
        _num(external, "payroll_forms_wages"), _num(current, "expense.wages"))

    history = sorted(package.get("historical_returns", []), key=lambda x: int(x["tax_year"]))
    if history:
        prior = history[-1].get("values", {})
        prior_receipts = _num(prior, "income.gross_receipts")
        prior_margin = _ratio(_num(prior, "income.gross_profit"), prior_receipts)
        features["receipt_change"] = _ratio(
            receipts - prior_receipts if receipts is not None and prior_receipts is not None else None,
            abs(prior_receipts) if prior_receipts is not None else None)
        features["gross_margin_change"] = (
            features["gross_margin"] - prior_margin
            if features["gross_margin"] is not None and prior_margin is not None else None)
        series = [*history, {"tax_year": package.get("tax_year"), "values": current}]
        features["loss_years"] = sum(
            (_num(item.get("values", {}), "taxable_income.total") or 0) < 0 for item in series)
    return features


def _above(context, key, threshold):
    return context["features"].get(key) is not None and context["features"][key] > threshold


def _absolute_above(context, key, threshold):
    value = context["features"].get(key)
    return value is not None and abs(value) > threshold


RULES = (
    IssueRule("INC-BANK-001", "selection_indicators", 22, "Deposits exceed reported receipts",
              lambda c: _above(c, "bank_deposit_ratio", 1.05),
              lambda c: f"Bank-deposit ratio is {c['features']['bank_deposit_ratio']:.3f}.",
              "Bank deposits and cash expenditures reconciliation",
              ("All bank statements", "Deposit ledger", "Loan and capital documents", "Books-to-return reconciliation"),
              ("Loan proceeds", "Capital contributions", "Transfers between accounts", "Returned checks or timing differences"),
              "Classify every deposit and document taxable and nontaxable sources.",
              ("IRM 4.10.4", "IRC 61", "IRC 6001")),
    IssueRule("INC-INFO-001", "selection_indicators", 20, "Information returns exceed receipts",
              lambda c: _above(c, "information_return_ratio", 1.02),
              lambda c: f"Information-return ratio is {c['features']['information_return_ratio']:.3f}.",
              "Third-party information-return reconciliation",
              ("Forms 1099/W-2/1099-K", "Issuer corrections", "Sales ledger", "Merchant statements"),
              ("Duplicate forms", "Nominee reporting", "Gross versus net processor reporting", "Timing difference"),
              "Reconcile every third-party form and pursue corrected forms where necessary.",
              ("IRM 4.1.27", "IRS Publication 556")),
    IssueRule("TREND-GM-001", "adjustment_exposure", 14, "Material gross-margin movement",
              lambda c: _absolute_above(c, "gross_margin_change", .10),
              lambda c: f"Gross margin changed {c['features']['gross_margin_change']:.1%} from the prior year.",
              "Horizontal analysis, COGS reconciliation, and cutoff testing",
              ("Sales detail", "Inventory records", "Purchase journal", "Pricing and product-mix analysis"),
              ("Product mix", "Price changes", "Supply disruption", "Inventory write-down", "Accounting-method change"),
              "Prepare a quantified bridge explaining price, volume, mix, and cost changes.",
              ("IRM 4.1.5", "Applicable industry ATG")),
    IssueRule("TREND-REV-001", "selection_indicators", 10, "Large year-over-year receipt movement",
              lambda c: _absolute_above(c, "receipt_change", .40),
              lambda c: f"Reported receipts changed {c['features']['receipt_change']:.1%}.",
              "Comparative-year analysis and revenue completeness testing",
              ("Monthly sales", "Contracts", "Bank deposits", "Acquisition/disposition documents"),
              ("Startup or closure", "Acquisition", "Lost major customer", "Short tax period", "Economic conditions"),
              "Document the operational causes and reconcile monthly activity.",
              ("IRM 4.1.5", "IRM 4.10.3")),
    IssueRule("DED-OTHER-001", "adjustment_exposure", 15, "High other-deduction concentration",
              lambda c: _above(c, "other_deduction_ratio", .20),
              lambda c: f"Other deductions are {c['features']['other_deduction_ratio']:.1%} of income.",
              "Disaggregate and substantiate other deductions",
              ("Other-deduction statement", "General ledger detail", "Invoices", "Contracts", "Payment evidence"),
              ("Legitimate aggregated accounts", "One-time professional fees", "Reclassification"),
              "Map every material amount to business purpose, authority, and primary evidence.",
              ("IRC 162", "IRC 6001", "IRM 4.10.3")),
    IssueRule("RPT-LOAN-001", "related_return", 16, "Material owner or related-party receivable",
              lambda c: _above(c, "related_receivable_asset_ratio", .10),
              lambda c: f"Related-party receivables are {c['features']['related_receivable_asset_ratio']:.1%} of assets.",
              "Trace related-party loans, distributions, compensation, and repayments",
              ("Notes", "Board approvals", "Payment history", "Imputed-interest analysis", "Related returns"),
              ("Bona fide working-capital advance", "Documented intercompany clearing account"),
              "Reconcile both sides and document terms, business purpose, interest, and repayment capacity.",
              ("IRC 267", "IRC 7872", "Applicable entity ATG")),
    IssueRule("PAY-REC-001", "adjustment_exposure", 16, "Payroll forms do not reconcile to wages",
              lambda c: c["features"].get("payroll_reconciliation_ratio") is not None and
                        abs(c["features"]["payroll_reconciliation_ratio"] - 1) > .02,
              lambda c: f"Payroll-to-ledger ratio is {c['features']['payroll_reconciliation_ratio']:.3f}.",
              "Reconcile payroll returns, W-2s, general ledger, and officer compensation",
              ("Forms 941/940/W-2", "Payroll register", "General ledger", "Contractor files"),
              ("Accrual timing", "Third-party payer", "Reclassification", "Non-wage benefits"),
              "Resolve differences by quarter and worker before assessing classification or tax exposure.",
              ("IRM 4.23", "IRC 3121")),
    IssueRule("LOSS-MULTI-001", "adjustment_exposure", 12, "Repeated reported losses",
              lambda c: (c["features"].get("loss_years") or 0) >= 3,
              lambda c: f"Losses appear in {c['features']['loss_years']} supplied years.",
              "Develop profit motive and verify activity classification",
              ("Business plan", "Time records", "Budgets", "Operational changes", "Evidence of expertise"),
              ("Startup phase", "Casualty", "Industry downturn", "Expansion costs"),
              "Document objective profit factors and corrective operational actions.",
              ("IRC 183", "Activities Not Engaged in for Profit ATG")),
    IssueRule("BASIS-001", "adjustment_exposure", 18, "Owner basis or loss limitation is incomplete",
              lambda c: bool(c["package"].get("issue_facts", {}).get("basis_support_incomplete")),
              lambda c: "The supplied facts identify an incomplete basis or loss-limitation schedule.",
              "Reconstruct basis, at-risk amounts, passive limitations, and distributions by year",
              ("Formation documents", "Annual K-1s", "Contribution and distribution records", "Debt documents", "Prior basis schedules"),
              ("Missing historical records", "Entity conversion", "Inherited or purchased interest"),
              "Prepare an immutable annual basis rollforward and reconcile it to entity and owner returns.",
              ("Forms 7203, 6198, and 8582", "Applicable entity instructions")),
    IssueRule("CAP-001", "adjustment_exposure", 15, "Repair versus capitalization treatment requires review",
              lambda c: bool(c["package"].get("issue_facts", {}).get("capitalization_uncertain")),
              lambda c: "The supplied facts identify material property costs with uncertain tax treatment.",
              "Apply unit-of-property, improvement, safe-harbor, disposition, and depreciation analysis",
              ("Invoices", "Project descriptions", "Fixed-asset ledger", "Capitalization policy", "Elections and Forms 4562"),
              ("Routine maintenance", "De minimis safe harbor", "Restoration after casualty"),
              "Classify each project and document elections and accounting treatment.",
              ("IRC 263(a)", "Treas. Reg. 1.263(a)-3", "Capitalization of Tangible Property ATG")),
    IssueRule("EMP-CLASS-001", "adjustment_exposure", 18, "Worker classification requires development",
              lambda c: bool(c["package"].get("issue_facts", {}).get("worker_classification_uncertain")),
              lambda c: "The supplied facts identify workers whose employee/contractor status is uncertain.",
              "Develop behavioral control, financial control, and relationship facts worker by worker",
              ("Contracts", "Invoices", "Work instructions", "Benefits", "Forms W-2/1099", "Comparable worker treatment"),
              ("Independent trade", "Project-based engagement", "Statutory classification or relief facts"),
              "Document the classification analysis and reconcile all employment and information returns.",
              ("IRM 4.23", "IRC 3121", "Section 530 relief review")),
    IssueRule("FOR-FORMS-001", "adjustment_exposure", 20, "Potential foreign information return is missing",
              lambda c: bool(c["package"].get("issue_facts", {}).get("foreign_form_expected_missing")),
              lambda c: "The supplied ownership or transaction facts indicate a potentially missing foreign form.",
              "Map foreign ownership, accounts, entities, transactions, and filing obligations",
              ("Entity chart", "Ownership ledger", "Foreign accounts", "Intercompany transactions", "Treaty positions", "Prior foreign forms"),
              ("Filing exception", "Indirect ownership below threshold", "Different reporting person"),
              "Obtain international specialist review before determining obligation, correction, or penalty exposure.",
              ("Applicable Forms 5471, 5472, 8865, 8858, 8938 and instructions", "IRM international procedures"),
              "International tax specialist and counsel review"),
    IssueRule("RDC-001", "adjustment_exposure", 17, "Research credit support is incomplete",
              lambda c: bool(c["package"].get("issue_facts", {}).get("research_credit_support_incomplete")),
              lambda c: "The supplied facts identify a research credit without complete project-level support.",
              "Test qualified activities, uncertainty, experimentation, business component, and qualified costs",
              ("Project list", "Technical records", "Payroll detail", "Contract research agreements", "Credit computation"),
              ("Contemporaneous engineering records available elsewhere", "Cost-center mapping issue"),
              "Build project-level nexus between activities, personnel, costs, and the filed computation.",
              ("IRC 41", "Research Credit Claims ATG")),
    IssueRule("DIGITAL-001", "adjustment_exposure", 14, "Digital-asset reporting requires reconciliation",
              lambda c: bool(c["package"].get("issue_facts", {}).get("digital_asset_reconciliation_missing")),
              lambda c: "The supplied facts identify digital-asset activity that is not fully reconciled.",
              "Reconcile wallets, exchanges, transfers, basis, proceeds, income, and information returns",
              ("Exchange exports", "Wallet addresses", "Transaction ledger", "Basis records", "Forms 1099"),
              ("Nontaxable wallet transfer", "Custodial movement", "Duplicate exchange reporting"),
              "Create a complete transaction ledger and document characterization and basis.",
              ("Applicable return question and instructions", "IRC 61", "IRC 1001")),
)


def assess_chief_audit(package: dict[str, Any]) -> dict[str, Any]:
    required = ("return_id", "form_family", "tax_year", "current_values")
    missing = [key for key in required if key not in package]
    if missing: raise ValueError(f"missing required fields: {', '.join(missing)}")
    features = calculate_features(package)
    context = {"package": package, "features": features}
    findings = [IssueFinding(rule.issue_id, rule.dimension, rule.weight, rule.title,
                             rule.fact(context), rule.audit_technique, rule.evidence,
                             rule.explanations, rule.remediation, rule.authorities, rule.review)
                for rule in RULES if rule.predicate(context)]
    dimensions = {name: min(100, sum(x.weight for x in findings if x.dimension == name))
                  for name in ("selection_indicators", "adjustment_exposure", "related_return")}
    controls = package.get("controls", {})
    documentation = round(100 * sum(bool(controls.get(k)) for k in
        ("books_reconciled", "third_party_reconciled", "support_complete", "retention_active")) / 4)
    internal = round(100 * sum(bool(controls.get(k)) for k in
        ("bank_reconciliations", "journal_approval", "payroll_controls", "related_party_approval")) / 4)
    controversy = round(100 * sum(bool(controls.get(k)) for k in
        ("statute_verified", "representative_authorized", "privilege_protocol", "preservation_active")) / 4)
    confidence_inputs = [package.get("current_values"), package.get("historical_returns"),
                         package.get("external_values"), package.get("reconciliation_report")]
    confidence = round(100 * sum(bool(x) for x in confidence_inputs) / len(confidence_inputs))
    reconciliation = package.get("reconciliation_report", {})
    gate = reconciliation.get("scoring_gate", "preliminary_only")
    priority = round(dimensions["selection_indicators"] * .25 + dimensions["adjustment_exposure"] * .35 +
                     dimensions["related_return"] * .10 + (100-documentation) * .15 +
                     (100-internal) * .10 + (100-controversy) * .05)
    evidence = sorted({item for finding in findings for item in finding.evidence})
    status = "final_review_ready" if gate == "eligible" and confidence >= 75 else "preliminary_only"
    return {"engine_version": ENGINE_VERSION, "return_id": str(package["return_id"]),
            "form_family": str(package["form_family"]), "tax_year": int(package["tax_year"]),
            "assessment_status": status, "features": features,
            "scores": {**dimensions, "documentation_readiness": documentation,
                       "internal_control_readiness": internal, "controversy_readiness": controversy,
                       "assessment_confidence": confidence, "portfolio_priority": priority},
            "findings": [asdict(x) for x in findings], "evidence_request": evidence,
            "scoring_gate": gate,
            "limitations": ["Portfolio priority is not an IRS audit probability.",
                            "Public indicators do not reproduce confidential IRS selection systems.",
                            "Potential explanations and defenses require evidence and professional review.",
                            "Penalty, fraud, privilege, and litigation conclusions require specialized review."]}
