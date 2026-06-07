using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap031Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP031",
        "Cyclomatic complexity exceeds the allowed maximum",
        "{0} has a cyclomatic complexity of {1}, which exceeds the maximum of {2} — extract methods or simplify branching to reduce complexity",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP031");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private const int MaxCyclomaticComplexity = 5;

    private static void AnalyzeCyclomaticComplexity(SyntaxNodeAnalysisContext context)
    {
        if (IsGeneratedFile(context.Node.SyntaxTree.FilePath))
        {
            return;
        }

        var (body, identifier, displayName) = context.Node switch
        {
            MethodDeclarationSyntax method => ((SyntaxNode?)method.Body ?? method.ExpressionBody, method.Identifier, $"Method '{method.Identifier.ValueText}'"),
            ConstructorDeclarationSyntax ctor => ((SyntaxNode?)ctor.Body ?? ctor.ExpressionBody, ctor.Identifier, $"Constructor '{ctor.Identifier.ValueText}'"),
            LocalFunctionStatementSyntax localFunction => ((SyntaxNode?)localFunction.Body ?? localFunction.ExpressionBody, localFunction.Identifier, $"Local function '{localFunction.Identifier.ValueText}'"),
            AccessorDeclarationSyntax accessor => ((SyntaxNode?)accessor.Body ?? accessor.ExpressionBody, accessor.Keyword, $"Accessor '{accessor.Keyword.ValueText}'"),
            _ => (null, default, string.Empty),
        };

        if (body is null)
        {
            return;
        }

        var complexity = CalculateCyclomaticComplexity(body);
        if (complexity <= MaxCyclomaticComplexity)
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap031Analyzer.Rule,
            identifier.GetLocation(),
            displayName,
            complexity,
            MaxCyclomaticComplexity));
    }

    private static int CalculateCyclomaticComplexity(SyntaxNode body)
    {
        // Base complexity of 1 (the single entry path), +1 per decision point.
        // Lambdas/anonymous functions count toward the enclosing member; nested
        // local functions are excluded because they are analyzed independently.
        var complexity = 1;

        foreach (var node in body.DescendantNodes(descendIntoChildren: n => !n.IsKind(SyntaxKind.LocalFunctionStatement)))
        {
            switch (node.Kind())
            {
                case SyntaxKind.IfStatement:
                case SyntaxKind.WhileStatement:
                case SyntaxKind.ForStatement:
                case SyntaxKind.ForEachStatement:
                case SyntaxKind.ForEachVariableStatement:
                case SyntaxKind.DoStatement:
                case SyntaxKind.CaseSwitchLabel:
                case SyntaxKind.CasePatternSwitchLabel:
                case SyntaxKind.CatchClause:
                case SyntaxKind.ConditionalExpression:
                case SyntaxKind.LogicalAndExpression:
                case SyntaxKind.LogicalOrExpression:
                case SyntaxKind.CoalesceExpression:
                case SyntaxKind.CoalesceAssignmentExpression:
                case SyntaxKind.OrPattern:
                    complexity++;
                    break;

                case SyntaxKind.SwitchExpressionArm:
                    // A discard arm is the default path, mirroring how a
                    // switch statement's `default:` label is not counted.
                    if (((SwitchExpressionArmSyntax)node).Pattern is not DiscardPatternSyntax)
                    {
                        complexity++;
                    }

                    break;
            }
        }

        return complexity;
    }
}
