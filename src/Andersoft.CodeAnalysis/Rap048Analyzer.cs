using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap048Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP048",
        "Suppressing RAP047 is not allowed",
        "Suppressing RAP047 with #pragma is not allowed; fix the violation instead",
        "Test Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterSyntaxTreeAction(AnalyzeSyntaxTree);
    }

    private static void AnalyzeSyntaxTree(SyntaxTreeAnalysisContext context)
    {
        var root = context.Tree.GetCompilationUnitRoot(context.CancellationToken);

        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            var text = trivia.ToFullString();

            if (text.IndexOf("#pragma", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (text.IndexOf("disable", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (text.IndexOf("RAP047", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, trivia.GetLocation()));
            }
        }
    }
}
