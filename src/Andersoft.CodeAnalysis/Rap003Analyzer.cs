using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap003Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP003",
        "Use TimeProvider instead of DateTime/DateTimeOffset now properties",
        "Use TimeProvider instead of DateTime/DateTimeOffset now properties",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP003");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeRap003SystemClockUsage(
        SyntaxNodeAnalysisContext context,
        MemberAccessExpressionSyntax memberAccess,
        string containingType,
        string propertyName,
        string layer)
    {
        Report(context, Diagnostic.Create(
            Rap003Analyzer.Rule,
            memberAccess.GetLocation(),
            $"{containingType}.{propertyName}",
            layer));
    }
}
