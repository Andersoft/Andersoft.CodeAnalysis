using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap016Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP016",
        "CancellationToken not propagated",
        "CancellationToken not propagated",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP016");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeRap016CancellationTokenPropagation(
        SyntaxNodeAnalysisContext context,
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol)
    {
        var tokenParameter = methodSymbol.Parameters.FirstOrDefault(static p => p.Type.ToDisplayString() == "System.Threading.CancellationToken");
        if (tokenParameter is null)
        {
            return;
        }

        foreach (var invocation in methodDeclaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (context.SemanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol called)
            {
                continue;
            }

            if (!called.Parameters.Any(static p => p.Type.ToDisplayString() == "System.Threading.CancellationToken"))
            {
                continue;
            }

            if (AnalyzeRap016InvocationPassesToken(invocation, called, tokenParameter.Name, context.SemanticModel))
            {
                continue;
            }

            Report(context, Diagnostic.Create(
                Rap016Analyzer.Rule,
                invocation.GetLocation(),
                methodSymbol.Name,
                called.Name));
        }
    }

    private static bool AnalyzeRap016InvocationPassesToken(
        InvocationExpressionSyntax invocation,
        IMethodSymbol called,
        string tokenName,
        SemanticModel semanticModel)
    {
        var ctParameter = called.Parameters.FirstOrDefault(static p => p.Type.ToDisplayString() == "System.Threading.CancellationToken");
        if (ctParameter is null)
        {
            return true;
        }

        if (ctParameter.Ordinal >= invocation.ArgumentList.Arguments.Count)
        {
            return false;
        }

        var positional = invocation.ArgumentList.Arguments[ctParameter.Ordinal];
        if (positional.NameColon is null)
        {
            return positional.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == tokenName;
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.NameColon?.Name.Identifier.ValueText != ctParameter.Name)
            {
                continue;
            }

            return argument.Expression is IdentifierNameSyntax namedId && namedId.Identifier.ValueText == tokenName;
        }

        _ = semanticModel;
        return false;
    }
}
