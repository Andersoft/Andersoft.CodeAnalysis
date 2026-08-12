using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap050Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP050",
        "Authorization role is hard-coded",
        "Role '{0}' is hard-coded — use a named authorization policy or a shared role constant",
        "Security",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context) =>
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP050");
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeHardCodedRole(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation ||
            IsGeneratedFile(invocation.SyntaxTree.FilePath) ||
            invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "IsInRole" } ||
            invocation.ArgumentList.Arguments.Count != 1 ||
            invocation.ArgumentList.Arguments[0].Expression is not LiteralExpressionSyntax literal ||
            literal.Token.Value is not string role)
        {
            return;
        }

        var method = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (method?.ContainingType.ToDisplayString() != "System.Security.Claims.ClaimsPrincipal")
        {
            return;
        }

        Report(context, Diagnostic.Create(Rap050Analyzer.Rule, literal.GetLocation(), role));
    }
}
