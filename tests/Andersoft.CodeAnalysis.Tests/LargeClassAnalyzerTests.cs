using Xunit;

namespace Andersoft.CodeAnalysis.Tests;

public sealed class LargeClassAnalyzerTests
{
    [Theory]
    [InlineData("Aggregate<System.Guid>")]
    [InlineData("Intermediate")]
    [InlineData("Domain.Aggregate<System.Guid>")]
    [InlineData("RoomAggregate")]
    public async Task AggregateDescendantsAreExemptFromBothSizeLimits(string baseType)
    {
        var source = $$"""
            using Domain;
            using RoomAggregate = Domain.Aggregate<System.Guid>;
            namespace Domain { public abstract class Aggregate<TId> { } }
            public abstract class Intermediate : Aggregate<System.Guid> { }
            public sealed class Room : {{baseType}}
            {
            {{Members}}
            {{new string('\n', 401)}}
            }
            """;

        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap037Analyzer()));
        var diagnostic = Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap068Analyzer()));
        Assert.Equal("RAP068", diagnostic.Id);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("logical domain boundaries", diagnostic.GetMessage());
        Assert.Contains("aggregate roots", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("public class Aggregate<TId> { }", "Aggregate<int>")]
    [InlineData("internal abstract class Aggregate<TId> { }", "Aggregate<int>")]
    [InlineData("public abstract class Aggregate { }", "Aggregate")]
    [InlineData("public abstract class Aggregate<TId, TValue> { }", "Aggregate<int, int>")]
    [InlineData("public abstract class Entity<TId> { }", "Entity<int>")]
    public async Task OtherBaseTypesRemainSubjectToTheRule(string declaration, string baseType)
    {
        var source = $$"""
            {{declaration}}
            internal sealed class Room : {{baseType}}
            {
            {{Members}}
            }
            """;

        var diagnostic = Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap037Analyzer()));

        Assert.Equal("RAP037", diagnostic.Id);
        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap068Analyzer()));
    }

    [Theory]
    [InlineData(25, 0, false)]
    [InlineData(26, 0, true)]
    [InlineData(0, 401, true)]
    public async Task OrdinaryClassesRetainSizeLimits(int members, int blankLines, bool expectedDiagnostic)
    {
        var source = "public class Room\n{\n" +
            string.Join("\n", Enumerable.Range(0, members).Select(i => $"public int Value{i} {{ get; set; }}")) +
            new string('\n', blankLines) + "\n}";

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(source, new Rap037Analyzer());

        Assert.Equal(expectedDiagnostic ? 1 : 0, diagnostics.Length);
        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap068Analyzer()));
    }

    [Theory]
    [InlineData(25, 396, false)]
    [InlineData(26, 396, true)]
    [InlineData(25, 397, true)]
    public async Task AggregateSizeLimitsMatchLargeClassLimits(int members, int blankLines, bool expectedDiagnostic)
    {
        var source = "public abstract class Aggregate<TId> { }\npublic class Room : Aggregate<int>\n{\n" +
            string.Join(" ", Enumerable.Range(0, members).Select(i => $"public int Value{i} {{ get; set; }}")) +
            new string('\n', blankLines) + "\n}";

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(source, new Rap068Analyzer());

        Assert.Equal(expectedDiagnostic ? 1 : 0, diagnostics.Length);
        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap037Analyzer()));
    }

    private static string Members => string.Join("\n",
        Enumerable.Range(0, 26).Select(i => $"public int Value{i} {{ get; set; }}"));
}
