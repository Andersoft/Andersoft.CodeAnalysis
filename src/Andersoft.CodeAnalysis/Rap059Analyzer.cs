using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap059Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP059", "Presentation layer repairs timestamp kind",
        "Method '{0}' calls DateTime.SpecifyKind — normalize timestamps at persistence or application boundaries and expose DateTimeOffset where possible",
        "Architecture", DiagnosticSeverity.Error, isEnabledByDefault: true);
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);
    public override void Initialize(AnalysisContext context) => ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP059");
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzePresentationTimestampRepair(SyntaxNodeAnalysisContext context)
    {
        if (!TryGetPresentationMethod(context, out var method)) return;
        foreach (var invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (context.SemanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { Name: "SpecifyKind" } symbol ||
                symbol.ContainingType.ToDisplayString() != "System.DateTime") continue;
            Report(context, Diagnostic.Create(Rap059Analyzer.Rule, invocation.GetLocation(), method.Identifier.ValueText));
        }
    }
}
