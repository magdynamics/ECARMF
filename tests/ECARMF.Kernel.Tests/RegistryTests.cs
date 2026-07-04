using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Tests;

public class RegistryTests
{
    private static RuleDeclaration Rule(string id, string trigger = "TransactionReceived", int priority = 100) => new()
    {
        RuleId = id,
        Name = id,
        TriggerEvent = trigger,
        Priority = priority,
        Conditions = [new RuleCondition { Field = "amount", Operator = ConditionOperator.GreaterThan, Value = "1" }],
        OutcomeOnMatch = "Flagged",
        ReasonTemplate = "test"
    };

    [Fact]
    public void Register_then_TryGet_returns_declaration_with_provenance()
    {
        var registry = new RuleRegistry();
        registry.Register(Rule("R-1"), "pkg.a", "1.0.0");

        Assert.True(registry.TryGet("R-1", out var found));
        Assert.Equal("pkg.a", found!.PackageId);
        Assert.Equal("1.0.0", found.PackageVersion);
        Assert.Equal("R-1", found.Declaration.RuleId);
    }

    [Fact]
    public void Register_duplicate_name_from_other_package_throws_conflict()
    {
        var registry = new RuleRegistry();
        registry.Register(Rule("R-1"), "pkg.a", "1.0.0");

        var ex = Assert.Throws<RegistryConflictException>(
            () => registry.Register(Rule("R-1"), "pkg.b", "2.0.0"));

        Assert.Equal("R-1", ex.ItemName);
        Assert.Equal("pkg.a", ex.OwningPackageId);
        Assert.Equal("1.0.0", ex.OwningPackageVersion);
    }

    [Fact]
    public void Register_same_name_from_same_package_version_also_conflicts()
    {
        var registry = new RuleRegistry();
        registry.Register(Rule("R-1"), "pkg.a", "1.0.0");

        Assert.Throws<RegistryConflictException>(
            () => registry.Register(Rule("R-1"), "pkg.a", "1.0.0"));
    }

    [Fact]
    public void UnregisterPackage_removes_only_that_packages_items()
    {
        var registry = new RuleRegistry();
        registry.Register(Rule("R-1"), "pkg.a", "1.0.0");
        registry.Register(Rule("R-2"), "pkg.b", "1.0.0");

        registry.UnregisterPackage("pkg.a", "1.0.0");

        Assert.False(registry.Contains("R-1"));
        Assert.True(registry.Contains("R-2"));
    }

    [Fact]
    public void UnregisterPackage_frees_name_for_new_version()
    {
        var registry = new RuleRegistry();
        registry.Register(Rule("R-1"), "pkg.a", "1.0.0");
        registry.UnregisterPackage("pkg.a", "1.0.0");

        registry.Register(Rule("R-1"), "pkg.a", "2.0.0");

        Assert.True(registry.TryGet("R-1", out var found));
        Assert.Equal("2.0.0", found!.PackageVersion);
    }

    [Fact]
    public void GetRulesForEvent_orders_by_priority_then_rule_id()
    {
        var registry = new RuleRegistry();
        registry.Register(Rule("R-B", priority: 200), "pkg.a", "1.0.0");
        registry.Register(Rule("R-C", priority: 100), "pkg.a", "1.0.0");
        registry.Register(Rule("R-A", priority: 200), "pkg.a", "1.0.0");
        registry.Register(Rule("R-X", trigger: "OtherEvent"), "pkg.a", "1.0.0");

        var rules = registry.GetRulesForEvent("TransactionReceived");

        Assert.Equal(["R-C", "R-A", "R-B"], rules.Select(r => r.Declaration.RuleId).ToArray());
    }

    [Fact]
    public void EventRegistry_IsDeclared_is_case_insensitive()
    {
        var registry = new EventRegistry();
        registry.Register(new EventDeclaration { EventName = "TransactionReceived" }, "pkg.a", "1.0.0");

        Assert.True(registry.IsDeclared("transactionreceived"));
        Assert.False(registry.IsDeclared("TransactionRejected"));
    }
}
