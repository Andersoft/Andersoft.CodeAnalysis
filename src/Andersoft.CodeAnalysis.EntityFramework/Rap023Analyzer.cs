using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System;

namespace Andersoft.CodeAnalysis.EntityFramework;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap023Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP023",
        "Migration name quality violation",
        "Migration name quality violation",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        EfAnalyzer.InitializeRule(context, "RAP023");
    }
}

internal static partial class EfAnalyzer
{
    private static void AnalyzeRap023MigrationNameQuality(SymbolAnalysisContext context, INamedTypeSymbol namedType)
    {
        var bannedTokens = new[] { "fix", "tmp", "test" };
        var lowerName = namedType.Name.ToLowerInvariant();
        foreach (var token in bannedTokens)
        {
            if (!lowerName.Contains(token, StringComparison.Ordinal))
            {
                continue;
            }

            Report(context, Diagnostic.Create(
                Rap023Analyzer.Rule,
                namedType.Locations[0],
                namedType.Name,
                token));
            break;
        }
    }
}
