using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap020Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP020",
        "Boundary mapping missing error code",
        "Boundary mapping missing error code",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP020");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeRap020ErrorCodePresence(
        SyntaxNodeAnalysisContext context,
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol)
    {
        if (!MethodUsesOneOfDispatch(methodDeclaration, context.SemanticModel))
        {
            return;
        }

        var methodText = methodDeclaration.ToString();
        var hasErrorCodeUsage = methodText.Contains("code", StringComparison.OrdinalIgnoreCase) ||
            methodText.Contains("ErrorCode", StringComparison.Ordinal);

        if (hasErrorCodeUsage)
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap020Analyzer.Rule,
            methodDeclaration.Identifier.GetLocation(),
            methodSymbol.Name));
    }
}
