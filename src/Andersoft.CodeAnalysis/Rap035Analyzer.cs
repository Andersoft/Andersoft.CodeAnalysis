using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap035Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP035",
        "N-Path complexity exceeds the allowed maximum",
        "{0} has an N-Path complexity of {1}, which exceeds the maximum of {2} — sequential branches multiply the number of execution paths; extract independent decision clusters into separate methods",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP035");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    /// <summary>
    /// Nejmeh's recommended limit (also PMD's default) for the number of
    /// acyclic execution paths through a member.
    /// </summary>
    private const long MaxNPathComplexity = 200;

    /// <summary>
    /// Path counts grow exponentially; saturate well below overflow so the
    /// multiplications stay safe.
    /// </summary>
    private const long NPathSaturation = long.MaxValue / 4;

    private static void AnalyzeNPathComplexity(SyntaxNodeAnalysisContext context)
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

        var nPath = body switch
        {
            BlockSyntax block => NPathOfStatement(block),
            ArrowExpressionClauseSyntax arrow => NPathSaturatedAdd(
                ExpressionPathFactor(arrow.Expression),
                CountShortCircuitOperators(arrow.Expression)),
            _ => 1L,
        };

        if (nPath <= MaxNPathComplexity)
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap035Analyzer.Rule,
            identifier.GetLocation(),
            displayName,
            nPath >= NPathSaturation ? $"more than {NPathSaturation}" : nPath.ToString(),
            MaxNPathComplexity));
    }

    /// <summary>
    /// Nejmeh's NPATH: statements in sequence multiply, branch alternatives
    /// add, loops contribute their body plus the zero-iteration path, and
    /// short-circuit operators in a predicate each add a path. Lambdas and
    /// nested local functions are path-counted independently, not here.
    /// </summary>
    private static long NPathOfStatement(StatementSyntax? statement)
    {
        switch (statement)
        {
            case null:
                return 1;

            case BlockSyntax block:
            {
                var product = 1L;
                foreach (var child in block.Statements)
                {
                    product = NPathSaturatedMultiply(product, NPathOfStatement(child));
                }

                return product;
            }

            case IfStatementSyntax ifStatement:
            {
                var paths = CountShortCircuitOperators(ifStatement.Condition);
                paths = NPathSaturatedAdd(paths, NPathOfStatement(ifStatement.Statement));
                paths = NPathSaturatedAdd(paths, NPathOfStatement(ifStatement.Else?.Statement));
                return paths;
            }

            case WhileStatementSyntax whileStatement:
                return NPathSaturatedAdd(
                    CountShortCircuitOperators(whileStatement.Condition) + 1,
                    NPathOfStatement(whileStatement.Statement));

            case DoStatementSyntax doStatement:
                return NPathSaturatedAdd(
                    CountShortCircuitOperators(doStatement.Condition) + 1,
                    NPathOfStatement(doStatement.Statement));

            case ForStatementSyntax forStatement:
                return NPathSaturatedAdd(
                    (forStatement.Condition is null ? 0 : CountShortCircuitOperators(forStatement.Condition)) + 1,
                    NPathOfStatement(forStatement.Statement));

            case CommonForEachStatementSyntax forEachStatement:
                return NPathSaturatedAdd(1, NPathOfStatement(forEachStatement.Statement));

            case SwitchStatementSyntax switchStatement:
            {
                var paths = CountShortCircuitOperators(switchStatement.Expression);
                var hasDefault = false;
                foreach (var section in switchStatement.Sections)
                {
                    hasDefault |= section.Labels.Any(label => label.IsKind(SyntaxKind.DefaultSwitchLabel));

                    var sectionPaths = 1L;
                    foreach (var sectionStatement in section.Statements)
                    {
                        sectionPaths = NPathSaturatedMultiply(sectionPaths, NPathOfStatement(sectionStatement));
                    }

                    paths = NPathSaturatedAdd(paths, sectionPaths);
                }

                // Without a default label, falling straight through is a path.
                return hasDefault ? paths : NPathSaturatedAdd(paths, 1);
            }

            case TryStatementSyntax tryStatement:
            {
                var paths = NPathOfStatement(tryStatement.Block);
                foreach (var catchClause in tryStatement.Catches)
                {
                    paths = NPathSaturatedAdd(paths, NPathOfStatement(catchClause.Block));
                }

                return tryStatement.Finally is null
                    ? paths
                    : NPathSaturatedMultiply(paths, NPathOfStatement(tryStatement.Finally.Block));
            }

            case UsingStatementSyntax usingStatement:
                return NPathOfStatement(usingStatement.Statement);

            case LockStatementSyntax lockStatement:
                return NPathOfStatement(lockStatement.Statement);

            case CheckedStatementSyntax checkedStatement:
                return NPathOfStatement(checkedStatement.Block);

            case UnsafeStatementSyntax unsafeStatement:
                return NPathOfStatement(unsafeStatement.Block);

            case FixedStatementSyntax fixedStatement:
                return NPathOfStatement(fixedStatement.Statement);

            case LabeledStatementSyntax labeledStatement:
                return NPathOfStatement(labeledStatement.Statement);

            case LocalFunctionStatementSyntax:
                return 1;

            // Straight-line statements (expression, declaration, return, throw,
            // yield, …) — branching expressions inside them still fork paths.
            default:
                return ExpressionPathFactor(statement);
        }
    }

    /// <summary>
    /// Path factor contributed by branching expressions (ternaries and switch
    /// expressions) inside a straight-line statement or expression body.
    /// </summary>
    private static long ExpressionPathFactor(SyntaxNode node)
    {
        var factor = 1L;
        foreach (var descendant in node.DescendantNodesAndSelf(n => !IsFunctionBoundary(n)))
        {
            if (descendant is ConditionalExpressionSyntax)
            {
                factor = NPathSaturatedMultiply(factor, 2);
            }
            else if (descendant is SwitchExpressionSyntax switchExpression)
            {
                factor = NPathSaturatedMultiply(factor, Math.Max(1, switchExpression.Arms.Count));
            }
        }

        return factor;
    }

    private static long CountShortCircuitOperators(ExpressionSyntax expression)
    {
        var count = 0L;
        foreach (var descendant in expression.DescendantNodesAndSelf(n => !IsFunctionBoundary(n)))
        {
            var kind = descendant.Kind();
            if (kind is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression)
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsFunctionBoundary(SyntaxNode node) =>
        node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax;

    private static long NPathSaturatedAdd(long left, long right) =>
        left >= NPathSaturation - right ? NPathSaturation : left + right;

    private static long NPathSaturatedMultiply(long left, long right) =>
        right != 0 && left >= NPathSaturation / right ? NPathSaturation : left * right;
}
