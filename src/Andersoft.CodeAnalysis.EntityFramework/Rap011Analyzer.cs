using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System;

namespace Andersoft.CodeAnalysis.EntityFramework;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap011Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP011",
        "Migration class placement violation",
        "Migration class placement violation",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        EfAnalyzer.InitializeRule(context, "RAP011");
    }
}

internal static partial class EfAnalyzer
{
    private static void AnalyzeRap011MigrationPlacement(
        SymbolAnalysisContext context,
        INamedTypeSymbol namedType,
        string filePath,
        INamedTypeSymbol migrationSymbol)
    {
        if (!InheritsOrImplements(namedType, migrationSymbol))
        {
            return;
        }

        var namespaceText = namedType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        var normalizedPath = filePath.Replace('\\', '/');
        var validNamespace = namespaceText.StartsWith("Api.Migrations", StringComparison.Ordinal);
        var validPath = normalizedPath.Contains("src/Api/Migrations/", StringComparison.OrdinalIgnoreCase);

        AnalyzeRap023MigrationNameQuality(context, namedType);

        if (validNamespace && validPath)
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap011Analyzer.Rule,
            namedType.Locations[0],
            namedType.Name));
    }
}
