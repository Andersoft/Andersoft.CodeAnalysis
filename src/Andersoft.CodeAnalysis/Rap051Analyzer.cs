using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap051Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP051",
        "Result mapping uses a catch-all arm",
        "Switch mapping for '{0}' uses a catch-all arm — map every result variant explicitly so new outcomes cannot silently become the wrong HTTP response",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context) =>
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP051");
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeCatchAllResultMapping(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not SwitchExpressionSyntax switchExpression ||
            IsGeneratedFile(switchExpression.SyntaxTree.FilePath) ||
            !GetContainingNamespace(switchExpression).Contains(".Presentation", StringComparison.Ordinal) ||
            !switchExpression.Arms.Any(arm => arm.Pattern is DiscardPatternSyntax))
        {
            return;
        }

        var resultType = context.SemanticModel.GetTypeInfo(switchExpression.GoverningExpression).Type;
        if (resultType is null || !resultType.Name.EndsWith("Result", StringComparison.Ordinal))
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap051Analyzer.Rule,
            switchExpression.SwitchKeyword.GetLocation(),
            resultType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }
}
