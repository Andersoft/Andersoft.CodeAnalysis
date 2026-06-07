using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap015Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP015",
        "Presentation try/catch for expected outcomes",
        "Presentation try/catch for expected outcomes",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP015");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeRap015PresentationTryCatchExpectedOutcome(
        SyntaxNodeAnalysisContext context,
        CatchClauseSyntax catchClause,
        MethodDeclarationSyntax methodDeclaration,
        INamedTypeSymbol caughtType)
    {
        Report(context, Diagnostic.Create(
            Rap015Analyzer.Rule,
            catchClause.Declaration!.Type.GetLocation(),
            methodDeclaration.Identifier.ValueText,
            caughtType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }
}
