using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap001Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP001",
        "Layer dependency violation",
        "Layer dependency violation",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP001");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeRap001LayerDependency(
        SyntaxNodeAnalysisContext context,
        UsingDirectiveSyntax usingDirective,
        string sourceNamespace,
        string targetNamespace)
    {
        if (!TryGetLayerViolation(sourceNamespace, targetNamespace, out var sourceLayer))
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap001Analyzer.Rule,
            usingDirective.GetLocation(),
            sourceLayer,
            targetNamespace));
    }
}
