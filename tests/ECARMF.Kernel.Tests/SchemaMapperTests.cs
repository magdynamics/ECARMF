using System.Text.Json;
using System.Text.Json.Serialization;
using ECARMF.Kernel.Application.Ingestion;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Tests;

/// <summary>
/// Mapping tests for every reference SchemaTemplate, using the exact sample
/// payloads from the Data Source Connector & Schema Template design spec:
/// raw payload in → correctly-populated target entity payload out.
/// </summary>
public class SchemaMapperTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static KnowledgePackageManifest LoadTemplatesManifest()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "packages", "connector-reference-templates-v1.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var json = File.ReadAllText(Path.Combine(directory!.FullName, "packages", "connector-reference-templates-v1.json"));
        var manifest = JsonSerializer.Deserialize<KnowledgePackageManifest>(json, JsonOptions);
        Assert.NotNull(manifest);
        return manifest!;
    }

    private static SchemaTemplateDeclaration Template(string templateId) =>
        LoadTemplatesManifest().SchemaTemplates.Single(t => t.TemplateId == templateId);

    [Fact]
    public void Reference_templates_manifest_validates()
    {
        var manifest = LoadTemplatesManifest();
        Assert.Equal(6, manifest.SchemaTemplates.Count);
        Assert.Empty(ManifestValidator.Validate(manifest, new EventRegistry()));
    }

    [Fact]
    public void Accounting_journal_entry_maps_to_transaction()
    {
        const string raw = """
        {
          "TxnId": "JE-10234",
          "TxnDate": "2026-06-15",
          "Line": [
            { "Account": "Cash - Operating", "Amount": -15000.00, "PostingType": "Credit" },
            { "Account": "Equipment - Renewable Energy", "Amount": 15000.00, "PostingType": "Debit" }
          ],
          "Memo": "Solar inverter purchase - Venture: SunridgeEnergy"
        }
        """;

        var result = SchemaMapper.Map(Template("accounting-journal-json"), raw);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var payload = Assert.Single(result.Records).Payload;
        Assert.Equal("JE-10234", payload["externalReferenceId"]);
        Assert.StartsWith("2026-06-15", payload["occurredAt"]);
        Assert.Equal("15000.00", payload["amount"]);
        Assert.Equal("SunridgeEnergy", payload["ventureId"]);
    }

    [Fact]
    public void Bank_mt940_statement_maps_to_treasury_event()
    {
        const string raw = ":61:2606150615D15000,00NTRFNONREF//Solar inverter payment\n:86:Beneficiary: SunridgeEnergy Equipment Supplier";

        var result = SchemaMapper.Map(Template("bank-mt940-text"), raw);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var payload = Assert.Single(result.Records).Payload;
        Assert.StartsWith("2026-06-15", payload["valueDate"]);
        Assert.Equal("Debit", payload["direction"]);
        Assert.Equal("15000.00", payload["amount"]);
        Assert.Equal("NONREF", payload["externalReferenceId"]);
        Assert.Contains("SunridgeEnergy", payload["counterparty"]);
    }

    [Fact]
    public void Broker_csv_trade_maps_with_consistent_amounts()
    {
        const string raw = "TradeDate,Symbol,Description,Quantity,Price,Amount,Action\n2026-06-10,REIT-CHI-04,Chicagoland Industrial REIT Units,500,102.35,51175.00,BUY";

        var result = SchemaMapper.Map(Template("broker-trades-csv"), raw);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var record = Assert.Single(result.Records);
        Assert.Equal("Purchase", record.Payload["transactionType"]);
        Assert.Equal("REIT-CHI-04", record.Payload["assetReference"]);
        // 500 * 102.35 = 51175.00 — consistent, no validation flag.
        Assert.False(record.Payload.ContainsKey("dataValidationFlag"));
        Assert.Empty(record.Warnings);
    }

    [Fact]
    public void Broker_csv_quantity_price_amount_mismatch_sets_validation_flag()
    {
        // 500 * 102.35 = 51175.00, but Amount claims 60000.00.
        const string raw = "TradeDate,Symbol,Description,Quantity,Price,Amount,Action\n2026-06-10,REIT-CHI-04,Chicagoland Industrial REIT Units,500,102.35,60000.00,BUY";

        var result = SchemaMapper.Map(Template("broker-trades-csv"), raw);

        Assert.True(result.Success);
        var record = Assert.Single(result.Records);
        // Flag, don't reject: the record maps but carries the finding.
        Assert.True(record.Payload.ContainsKey("dataValidationFlag"));
        Assert.Contains(record.Warnings, w => w.Contains("Consistency check failed"));
    }

    [Fact]
    public void Siteview_event_maps_to_operational_event()
    {
        const string raw = """
        {
          "equipmentId": "EXC-2214",
          "siteId": "Bridgeview-Lot7",
          "eventType": "MaintenanceCompleted",
          "timestamp": "2026-06-20T14:32:00Z",
          "downtimeHours": 6.5,
          "cost": 1200.00
        }
        """;

        var result = SchemaMapper.Map(Template("siteview-events-json"), raw);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var payload = Assert.Single(result.Records).Payload;
        Assert.Equal("EXC-2214", payload["assetReference"]);
        Assert.Equal("MaintenanceCompleted", payload["eventCategory"]);
        Assert.Equal("6.5", payload["downtimeHours"]);
    }

    [Fact]
    public void Manual_opportunity_form_maps_to_opportunity()
    {
        const string raw = """
        {
          "opportunityType": "RealEstateAcquisition",
          "title": "Former Kmart site - Orland Park redevelopment",
          "estimatedValue": 4200000,
          "submittedBy": "user:mag",
          "notes": "Comparable to Cubework/Dick's transaction structure"
        }
        """;

        var result = SchemaMapper.Map(Template("manual-opportunity-json"), raw);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var payload = Assert.Single(result.Records).Payload;
        Assert.Equal("RealEstateAcquisition", payload["opportunitySubtype"]);
        Assert.Equal("4200000", payload["estimatedValue"]);
        Assert.Contains("Cubework", payload["notes"]);
    }

    [Fact]
    public void Market_risk_provider_feed_maps_with_interval_preserved()
    {
        const string raw = """
        {
          "modelId": "MarketRiskIndex-v3",
          "assetClass": "CommercialRealEstate-Midwest",
          "riskScore": 0.62,
          "confidenceInterval": [0.55, 0.69],
          "asOf": "2026-07-01T00:00:00Z",
          "modelProvider": "ExternalAnalyticsCo"
        }
        """;

        var result = SchemaMapper.Map(Template("market-risk-provider-json"), raw);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var payload = Assert.Single(result.Records).Payload;
        Assert.Equal("MarketRiskIndex-v3", payload["modelId"]);
        Assert.Equal("0.62", payload["value"]);
        // The interval is stored as-is, never collapsed to a single number.
        Assert.Equal("[0.55, 0.69]", payload["confidenceInterval"]);
        Assert.Equal("ExternalAnalyticsCo", payload["provider"]);
    }

    [Fact]
    public void Missing_required_field_is_an_error_not_a_silent_gap()
    {
        var result = SchemaMapper.Map(Template("accounting-journal-json"), """{ "Memo": "no id" }""");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("externalReferenceId"));
    }
}
