"""Conservative schedule expectations used for completeness review."""

def expected_schedules(return_record: dict) -> list[dict[str, str]]:
    """Return review expectations, not a legal filing determination."""
    form, flags = str(return_record.get("form_family", "")), return_record.get("flags", {})
    expected: list[dict[str, str]] = []
    def add(schedule: str, reason: str, status: str = "expected"):
        expected.append({"schedule": schedule, "reason": reason, "status": status})
    if form == "1040":
        for flag, schedule in (("business_activity", "Schedule C"),
                               ("rental_or_passthrough_activity", "Schedule E"),
                               ("farm_activity", "Schedule F"),
                               ("capital_transactions", "Schedule D")):
            if flags.get(flag): add(schedule, f"{flag.replace('_', ' ').title()} was identified.")
    elif form in {"1065", "1120-S"}:
        add("Schedule K", "Pass-through entity return.")
        add("Schedule K-1", "Owner allocations must be supported.")
        if flags.get("schedule_l_required", True):
            for schedule in ("Schedule L", "Schedule M-1", "Schedule M-2"):
                add(schedule, "Balance sheet, book-tax, and capital rollforward review is expected.")
        if flags.get("m3_required"):
            add("Schedule M-3", "Supplied facts indicate possible M-3 applicability.", "requires_rule_review")
    elif form == "1120":
        if flags.get("schedule_l_required", True):
            add("Schedule L", "Corporate balance-sheet review is expected.")
            add("Schedule M-1", "Book-to-tax reconciliation is expected unless M-3 applies.")
        if flags.get("m3_required"):
            add("Schedule M-3", "Supplied facts indicate possible M-3 applicability.", "requires_rule_review")
    elif form == "1041" and flags.get("beneficiary_distributions"):
        add("Schedule K-1", "Beneficiary distributions or allocations were identified.")
    elif form.startswith("990") and flags.get("unrelated_business_income"):
        add("Form 990-T", "Unrelated business income was identified.", "requires_rule_review")
    return expected
