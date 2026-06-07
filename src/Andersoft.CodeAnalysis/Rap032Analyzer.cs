using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap032Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP032",
        "Cognitive complexity exceeds the allowed maximum",
        "{0} has a cognitive complexity of {1}, which exceeds the maximum of {2} — flatten nesting (early returns, guard clauses) or extract methods to make the control flow easier to follow",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP032");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private const int MaxCognitiveComplexity = 15;

    private static void AnalyzeCognitiveComplexity(SyntaxNodeAnalysisContext context)
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

        var walker = new CognitiveComplexityWalker();
        walker.Visit(body);

        if (walker.Complexity <= MaxCognitiveComplexity)
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap032Analyzer.Rule,
            identifier.GetLocation(),
            displayName,
            walker.Complexity,
            MaxCognitiveComplexity));
    }

    /// <summary>
    /// Computes cognitive complexity following the Sonar specification:
    /// each control-flow structure costs +1 plus the current nesting depth,
    /// `else`/`else if` cost a flat +1, each sequence of like logical
    /// operators costs +1, and lambdas add nesting without an increment.
    /// Nested local functions are skipped because they are analyzed
    /// independently.
    /// </summary>
    private sealed class CognitiveComplexityWalker : CSharpSyntaxWalker
    {
        private int _nesting;

        public int Complexity { get; private set; }

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            // `else if` costs a flat +1 (no nesting penalty), a plain `if`
            // costs +1 plus the current nesting depth.
            Complexity += node.Parent is ElseClauseSyntax ? 1 : 1 + _nesting;

            Visit(node.Condition);
            VisitNested(node.Statement);

            if (node.Else is null)
            {
                return;
            }

            if (node.Else.Statement is IfStatementSyntax elseIf)
            {
                Visit(elseIf);
            }
            else
            {
                Complexity += 1;
                VisitNested(node.Else.Statement);
            }
        }

        public override void VisitWhileStatement(WhileStatementSyntax node)
        {
            Complexity += 1 + _nesting;
            Visit(node.Condition);
            VisitNested(node.Statement);
        }

        public override void VisitDoStatement(DoStatementSyntax node)
        {
            Complexity += 1 + _nesting;
            VisitNested(node.Statement);
            Visit(node.Condition);
        }

        public override void VisitForStatement(ForStatementSyntax node)
        {
            Complexity += 1 + _nesting;

            if (node.Declaration is not null)
            {
                Visit(node.Declaration);
            }

            foreach (var initializer in node.Initializers)
            {
                Visit(initializer);
            }

            if (node.Condition is not null)
            {
                Visit(node.Condition);
            }

            foreach (var incrementor in node.Incrementors)
            {
                Visit(incrementor);
            }

            VisitNested(node.Statement);
        }

        public override void VisitForEachStatement(ForEachStatementSyntax node)
        {
            Complexity += 1 + _nesting;
            Visit(node.Expression);
            VisitNested(node.Statement);
        }

        public override void VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
        {
            Complexity += 1 + _nesting;
            Visit(node.Expression);
            VisitNested(node.Statement);
        }

        public override void VisitSwitchStatement(SwitchStatementSyntax node)
        {
            // The switch as a whole costs +1 plus nesting; individual case
            // labels are free (unlike cyclomatic complexity).
            Complexity += 1 + _nesting;
            Visit(node.Expression);

            _nesting++;
            foreach (var section in node.Sections)
            {
                Visit(section);
            }

            _nesting--;
        }

        public override void VisitSwitchExpression(SwitchExpressionSyntax node)
        {
            Complexity += 1 + _nesting;
            Visit(node.GoverningExpression);

            _nesting++;
            foreach (var arm in node.Arms)
            {
                Visit(arm);
            }

            _nesting--;
        }

        public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            Complexity += 1 + _nesting;
            Visit(node.Condition);

            _nesting++;
            Visit(node.WhenTrue);
            Visit(node.WhenFalse);
            _nesting--;
        }

        public override void VisitCatchClause(CatchClauseSyntax node)
        {
            Complexity += 1 + _nesting;
            _nesting++;
            base.VisitCatchClause(node);
            _nesting--;
        }

        public override void VisitGotoStatement(GotoStatementSyntax node)
        {
            Complexity += 1;
            base.VisitGotoStatement(node);
        }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            // Each sequence of like logical operators costs +1: `a && b && c`
            // is +1, `a && b || c` is +2. Only the head of a run is counted —
            // an operand whose parent is the same operator kind is part of an
            // already-counted sequence.
            var kind = node.Kind();
            if (kind is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression &&
                !IsContinuationOfSameOperator(node, kind))
            {
                Complexity += 1;
            }

            base.VisitBinaryExpression(node);
        }

        public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            VisitNested(node.Body);
        }

        public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            if (node.Body is not null)
            {
                VisitNested(node.Body);
            }
        }

        public override void VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
        {
            VisitNested(node.Body);
        }

        public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            // Analyzed independently — do not descend.
        }

        private void VisitNested(SyntaxNode node)
        {
            _nesting++;
            Visit(node);
            _nesting--;
        }

        private static bool IsContinuationOfSameOperator(BinaryExpressionSyntax node, SyntaxKind kind)
        {
            var parent = node.Parent;
            while (parent is ParenthesizedExpressionSyntax)
            {
                parent = parent.Parent;
            }

            return parent is BinaryExpressionSyntax parentBinary && parentBinary.Kind() == kind;
        }
    }
}
