using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap021Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP021",
        "Sensitive data logged",
        "Sensitive data logged",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP021");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeRap021SensitiveLogging(
        SyntaxNodeAnalysisContext context,
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol)
    {
        var bannedKeys = new[] { "token", "password", "secret", "apikey", "api_key", "credential", "authorization" };

        foreach (var invocation in methodDeclaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (context.SemanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol called)
            {
                continue;
            }

            if (!called.Name.StartsWith("Log", StringComparison.Ordinal) ||
                !called.ContainingType.Name.Contains("Logger", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var arg in invocation.ArgumentList.Arguments)
            {
                var text = arg.ToString();
                var key = bannedKeys.FirstOrDefault(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
                if (key is null)
                {
                    continue;
                }

                Report(context, Diagnostic.Create(
                    Rap021Analyzer.Rule,
                    arg.GetLocation(),
                    methodSymbol.Name,
                    key));
                break;
            }
        }
    }
}
