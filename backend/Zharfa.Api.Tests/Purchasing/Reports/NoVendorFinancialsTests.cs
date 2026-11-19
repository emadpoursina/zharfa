namespace Zharfa.Api.Tests.Purchasing.Reports;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;
using Zharfa.Api.Purchasing.Reports;

/// <summary>
/// FR-012: no vendor balance, payment, credit, debt or paid figure exists in any type of the purchasing reports
/// namespace. The types are found by an assembly scan of the namespace (not a fixed file list), so a DTO added
/// later is covered automatically.
/// Note (Phase 6, T045/T065): Financial DTO types live strictly in Zharfa.Api.VendorFinancials, never here.
/// </summary>
public class NoVendorFinancialsTests
{
    private static readonly string[] ForbiddenWords = { "balance", "payment", "debt", "credit", "paid" };

    [Fact]
    public void NoTypeOfTheReportsNamespaceHasAVendorFinancialProperty()
    {
        var assembly = typeof(ReportCatalog).Assembly;
        var types = assembly.GetTypes()
            .Where(t => string.Equals(t.Namespace, typeof(ReportCatalog).Namespace, StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(types);

        var offenders = new List<string>();
        foreach (var type in types)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (ForbiddenWords.Any(word => property.Name.Contains(word, StringComparison.OrdinalIgnoreCase)))
                {
                    offenders.Add(type.FullName + "." + property.Name);
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void ResponseRecordsCoverTheDirectoryProfileAndComparisonTypes()
    {
        var names = typeof(ReportCatalog).Assembly.GetTypes()
            .Where(t => string.Equals(t.Namespace, typeof(ReportCatalog).Namespace, StringComparison.Ordinal))
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("VendorRow", names);
        Assert.Contains("VendorProfileResponse", names);
        Assert.Contains("VendorComparisonResponse", names);
        Assert.Contains("DocumentLinesResponse", names);
    }
}
