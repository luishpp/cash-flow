using System.Reflection;
using FluentAssertions;

namespace CashFlow.Architecture.Tests;

/// <summary>
/// Rich Domain Model exige encapsulamento: entidades não devem expor setters públicos (ADR-009).
/// NetArchTest 1.3.2 não tem helper específico para isso — usamos reflection direta.
/// </summary>
public class ImmutabilityTests
{
    [Fact]
    public void Transactions_Entities_MustNotHavePublicSetters()
    {
        var violations = FindEntitiesWithPublicSetters(
            typeof(CashFlow.Transactions.API.Domain.Entities.Transaction).Assembly,
            "CashFlow.Transactions.API.Domain.Entities");

        violations.Should().BeEmpty(
            "Transactions entities must have only private setters (Rich Domain).");
    }

    [Fact]
    public void Balance_Entities_MustNotHavePublicSetters()
    {
        var violations = FindEntitiesWithPublicSetters(
            typeof(CashFlow.Balance.API.Domain.Entities.DailyBalance).Assembly,
            "CashFlow.Balance.API.Domain.Entities");

        violations.Should().BeEmpty(
            "Balance entities must have only private setters (Rich Domain).");
    }

    private static List<string> FindEntitiesWithPublicSetters(Assembly assembly, string @namespace)
    {
        var violations = new List<string>();

        var entityTypes = assembly.GetTypes()
            .Where(t => t.Namespace == @namespace
                        && t.IsClass
                        && !t.IsAbstract
                        && !t.Name.StartsWith("<>"));

        foreach (var type in entityTypes)
        {
            var publicSetters = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => new { Property = p, Setter = p.GetSetMethod(nonPublic: false) })
                .Where(x => x.Setter is not null)
                .Select(x => x.Property.Name)
                .ToList();

            if (publicSetters.Count > 0)
            {
                violations.Add($"{type.FullName}: [{string.Join(", ", publicSetters)}]");
            }
        }

        return violations;
    }
}
