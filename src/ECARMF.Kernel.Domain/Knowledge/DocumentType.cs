namespace ECARMF.Kernel.Domain.Knowledge;

/// <summary>
/// A canonical document type the platform understands — a bank statement, a
/// W-2, a financial statement, an IRS letter. Platform-level knowledge reused
/// across every tenant: the SAME bank statement means the same thing for a
/// dental group and a restaurant. Each type declares its purpose and the
/// fields that matter (with which are numeric and aggregatable), so the triage
/// classifier can name it, extraction knows what to pull, and reconciliation
/// tasks know what can be summed.
/// </summary>
public sealed record DocumentType(
    string TypeKey,
    string Name,
    string Category,
    string Purpose,
    IReadOnlyList<DocumentField> KeyFields,
    IReadOnlyList<string> Aliases,
    string? TypicalIssuer = null);

/// <summary>One field a document type carries.</summary>
/// <param name="Aggregatable">True for numeric fields a reconciliation task
/// may sum/average (deposits, wages) — the platform, never the model, does
/// the arithmetic.</param>
public sealed record DocumentField(
    string Name, string Description, string DataType, bool Aggregatable = false);

/// <summary>
/// The curated canonical library. Platform reference data (not per-tenant):
/// the set every tenant inherits. Extend by adding entries here; tenant-custom
/// types are a later extension on top of this base.
/// </summary>
public static class DocumentTypeCatalog
{
    public static readonly IReadOnlyList<DocumentType> All =
    [
        new("bank-statement", "Bank Statement", "Financial",
            "A periodic account statement from a bank showing deposits, withdrawals, and balances for one account over a period. Basis for cash-flow reconciliation and deposit verification.",
            [
                new("bankName", "Issuing bank (e.g. Chase, Bank of America)", "string"),
                new("accountNumber", "Account number (often masked)", "string"),
                new("statementPeriod", "Period covered (e.g. 2026-06)", "period"),
                new("depositsTotal", "Total deposits in the period", "money", Aggregatable: true),
                new("withdrawalsTotal", "Total withdrawals in the period", "money", Aggregatable: true),
                new("endingBalance", "Balance at period end", "money"),
                new("depositLines", "Individual deposit line items (date, amount, description)", "lineItems", Aggregatable: true),
            ],
            ["bank statement", "account statement", "monthly statement", "boa statement", "chase statement"],
            "Bank"),

        new("w2", "W-2 Wage and Tax Statement", "Tax",
            "IRS Form W-2: an employer's annual report of one employee's wages and the taxes withheld for a tax year. Basis for wage totals and withholding verification.",
            [
                new("employeeName", "Employee name", "string"),
                new("employerName", "Employer name", "string"),
                new("taxYear", "Tax year", "year"),
                new("wages", "Box 1 — wages, tips, other compensation", "money", Aggregatable: true),
                new("federalWithheld", "Box 2 — federal income tax withheld", "money", Aggregatable: true),
                new("socialSecurityWages", "Box 3 — Social Security wages", "money", Aggregatable: true),
                new("medicareWages", "Box 5 — Medicare wages", "money", Aggregatable: true),
            ],
            ["w2", "w-2", "wage and tax statement", "form w2"],
            "Employer / IRS"),

        new("form-1040", "Form 1040 — Individual Income Tax Return", "Tax",
            "IRS Form 1040: an individual's annual federal income tax return. Basis for AGI, total tax, and refund/balance-due review.",
            [
                new("taxpayerName", "Taxpayer name", "string"),
                new("taxYear", "Tax year", "year"),
                new("adjustedGrossIncome", "Adjusted gross income (AGI)", "money", Aggregatable: true),
                new("totalTax", "Total tax", "money", Aggregatable: true),
                new("refundOrOwed", "Refund (positive) or balance due (negative)", "money"),
            ],
            ["1040", "form 1040", "individual tax return", "income tax return"],
            "Taxpayer / IRS"),

        new("financial-statement", "Financial Statement", "Financial",
            "A business financial statement (income statement, balance sheet, or cash flow) for a period. Basis for ratio analysis and financial-risk indicators.",
            [
                new("statementType", "IncomeStatement | BalanceSheet | CashFlow", "string"),
                new("subjectEntity", "Whose statement (entity/client)", "string"),
                new("period", "Period covered", "period"),
                new("revenue", "Total revenue", "money", Aggregatable: true),
                new("netIncome", "Net income", "money", Aggregatable: true),
                new("totalAssets", "Total assets", "money", Aggregatable: true),
                new("totalLiabilities", "Total liabilities", "money", Aggregatable: true),
            ],
            ["financial statement", "income statement", "balance sheet", "cash flow statement", "p&l", "profit and loss"],
            null),

        new("irs-letter", "IRS Notice / Letter", "Tax",
            "Correspondence from the IRS (a notice or letter, e.g. CP2000, audit notice, balance due). Basis for compliance-deadline and liability tracking.",
            [
                new("noticeNumber", "Notice/letter number (e.g. CP2000)", "string"),
                new("taxpayerName", "Taxpayer/entity addressed", "string"),
                new("taxYear", "Tax year at issue", "year"),
                new("amountDue", "Amount the IRS says is due", "money", Aggregatable: true),
                new("responseDeadline", "Deadline to respond", "date"),
            ],
            ["irs letter", "irs notice", "cp2000", "cp notice", "audit notice", "balance due notice"],
            "IRS"),

        new("pay-stub", "Pay Stub / Earnings Statement", "Financial",
            "An employee's per-pay-period earnings statement showing gross pay, deductions, and net pay. Basis for payroll verification and period wage totals.",
            [
                new("employeeName", "Employee name", "string"),
                new("payPeriod", "Pay period", "period"),
                new("grossPay", "Gross pay for the period", "money", Aggregatable: true),
                new("netPay", "Net pay for the period", "money", Aggregatable: true),
                new("deductions", "Total deductions", "money", Aggregatable: true),
            ],
            ["pay stub", "paystub", "earnings statement", "payroll stub"],
            "Employer"),

        new("invoice", "Invoice", "Financial",
            "A vendor bill for goods or services. Basis for accounts-payable totals and vendor-spend reconciliation.",
            [
                new("vendorName", "Vendor/supplier", "string"),
                new("invoiceNumber", "Invoice number", "string"),
                new("invoiceDate", "Invoice date", "date"),
                new("amount", "Invoice total", "money", Aggregatable: true),
            ],
            ["invoice", "bill", "vendor invoice"],
            null),

        new("lease", "Lease / Contract", "Legal",
            "A lease or contract with renewal and obligation terms. Basis for renewal tracking and commitment monitoring.",
            [
                new("counterparty", "Landlord/lessor or counterparty", "string"),
                new("subject", "What is leased/contracted", "string"),
                new("term", "Term length / dates", "string"),
                new("periodicAmount", "Rent/payment per period", "money", Aggregatable: true),
                new("renewalDate", "Renewal/expiry date", "date"),
            ],
            ["lease", "rental agreement", "contract", "equipment lease"],
            null),
    ];

    public static DocumentType? Find(string typeKey) =>
        All.FirstOrDefault(t => string.Equals(t.TypeKey, typeKey, StringComparison.OrdinalIgnoreCase));

    /// <summary>Compact one-line-per-type summary for prompting the classifier.</summary>
    public static string ForPrompt() =>
        string.Join("\n", All.Select(t => $"- {t.TypeKey}: {t.Name} ({t.Category}) — {t.Purpose}"));
}
