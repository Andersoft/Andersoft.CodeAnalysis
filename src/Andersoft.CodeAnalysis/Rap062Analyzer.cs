using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap062Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP062",
        "Control flow, functions, and braces must use block formatting",
        "{0}",
        "Style",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context) =>
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP062");
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeIfStatementBody(SyntaxNodeAnalysisContext context)
    {
        var ifStatement = (IfStatementSyntax)context.Node;
        if (ifStatement.Statement is BlockSyntax)
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap062Analyzer.Rule,
            ifStatement.IfKeyword.GetLocation(),
            "If statement body must be enclosed in braces"));
    }

    private static void AnalyzeExpressionBodiedMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        if (method.ExpressionBody is null)
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap062Analyzer.Rule,
            method.ExpressionBody.ArrowToken.GetLocation(),
            "Method body must be enclosed in braces"));
    }

    private static void AnalyzeExpressionBodiedLambda(SyntaxNodeAnalysisContext context)
    {
        var lambda = (LambdaExpressionSyntax)context.Node;
        if (lambda.Body is BlockSyntax || IsNestedInFunction(lambda))
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap062Analyzer.Rule,
            lambda.ArrowToken.GetLocation(),
            "Lambda body must be enclosed in braces unless the lambda is declared within a function"));
    }

    private static bool IsNestedInFunction(LambdaExpressionSyntax lambda) =>
        lambda.Ancestors().Any(node => node is
            BaseMethodDeclarationSyntax or
            LocalFunctionStatementSyntax or
            AccessorDeclarationSyntax or
            AnonymousFunctionExpressionSyntax or
            GlobalStatementSyntax);

    private static void AnalyzeBracePlacement(SyntaxTreeAnalysisContext context)
    {
        var root = context.Tree.GetRoot(context.CancellationToken);
        var text = context.Tree.GetText(context.CancellationToken);

        foreach (var brace in root.DescendantTokens().Where(token =>
                     token.IsKind(SyntaxKind.OpenBraceToken) || token.IsKind(SyntaxKind.CloseBraceToken)))
        {
            var line = text.Lines.GetLineFromPosition(brace.SpanStart);
            if (line.ToString().Trim() == brace.Text)
            {
                continue;
            }

            Report(context, Diagnostic.Create(
                Rap062Analyzer.Rule,
                brace.GetLocation(),
                $"Brace '{brace.Text}' must be on its own line"));
        }
    }
}
