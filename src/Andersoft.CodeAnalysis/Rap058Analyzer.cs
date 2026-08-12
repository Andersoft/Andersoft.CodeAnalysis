using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap058Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP058", "JSON response is manually serialized text",
        "Method '{0}' returns literal JSON through Results.Text — return a typed object through Results.Json, Ok, or a typed result",
        "Architecture", DiagnosticSeverity.Error, isEnabledByDefault: true);
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);
    public override void Initialize(AnalysisContext context) => ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP058");
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeManualJsonTextResponse(SyntaxNodeAnalysisContext context)
    {
        if (!TryGetPresentationMethod(context, out var method)) return;
        foreach (var invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Text" } ||
                invocation.ArgumentList.Arguments.Count < 2 ||
                invocation.ArgumentList.Arguments[0].Expression is not LiteralExpressionSyntax payload ||
                invocation.ArgumentList.Arguments[1].Expression is not LiteralExpressionSyntax mediaType ||
                mediaType.Token.ValueText != "application/json") continue;
            Report(context, Diagnostic.Create(Rap058Analyzer.Rule, payload.GetLocation(), method.Identifier.ValueText));
        }
    }
}
