using System.Reflection;
using AidlyPms.Api.Common;
using Xunit;

namespace AidlyPms.Api.Tests;

public class StockMovementTypesTests
{
    [Fact]
    public void StockMovementTypes_MustHaveUniqueCodesAcrossAllNamedTypes()
    {
        var fields = typeof(StockMovementTypes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly && (f.FieldType == typeof(short) || f.FieldType == typeof(int)))
            .ToList();

        Assert.NotEmpty(fields);

        var values = new Dictionary<short, string>();
        var duplicates = new List<string>();

        foreach (var field in fields)
        {
            var value = (short)field.GetValue(null)!;
            if (values.TryGetValue(value, out var existingField))
            {
                duplicates.Add($"Types '{existingField}' and '{field.Name}' share code {value}.");
            }
            else
            {
                values[value] = field.Name;
            }
        }

        Assert.True(duplicates.Count == 0,
            $"Duplicate stock movement codes found: {string.Join("; ", duplicates)}");
    }

    [Fact]
    public void StockMovementTypes_MustContainAllElevenMandatedTypes()
    {
        var expectedNames = new[]
        {
            "Opening",
            "PurchaseReceipt",
            "PosSale",
            "CustomerReturn",
            "VoidRestock",
            "SupplierReturn",
            "Adjustment",
            "StockTake",
            "TransferOut",
            "TransferIn",
            "Settlement"
        };

        var actualNames = typeof(StockMovementTypes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly)
            .Select(f => f.Name)
            .ToHashSet();

        foreach (var name in expectedNames)
        {
            Assert.Contains(name, actualNames);
        }
    }

    [Fact]
    public void UniquenessValidator_FailsWhenTwoNamedTypesShareACode()
    {
        var mockMovementTypes = new (string Name, short Code)[]
        {
            ("Opening", 1),
            ("PurchaseReceipt", 2),
            ("DuplicateType", 1) // Duplicate code 1
        };

        var duplicates = mockMovementTypes
            .GroupBy(t => t.Code)
            .Where(g => g.Count() > 1)
            .Select(g => $"Code {g.Key} shared by {string.Join(" and ", g.Select(x => x.Name))}")
            .ToList();

        Assert.NotEmpty(duplicates);
        Assert.Single(duplicates);
        Assert.Equal("Code 1 shared by Opening and DuplicateType", duplicates[0]);
    }
}
