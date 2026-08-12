using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap060Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP060", "Endpoint performs authorization inside the handler",
        "Endpoint handler '{0}' returns {1} directly — enforce authorization with an endpoint policy or authorization requirement",
        "Security", DiagnosticSeverity.Error, isEnabledByDefault: true);
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);
    public override void Initialize(AnalysisContext context) => ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP060");
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeHandlerLocalAuthorization(SyntaxNodeAnalysisContext context)
    {
        if (!TryGetPresentationMethod(context, out var method) || !IsEndpointHandler(method)) return;
        foreach (var invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax member ||
                member.Name.Identifier.ValueText is not ("Unauthorized" or "Forbid")) continue;
            Report(context, Diagnostic.Create(Rap060Analyzer.Rule, invocation.GetLocation(), method.Identifier.ValueText, member.Name.Identifier.ValueText));
        }
    }
}
