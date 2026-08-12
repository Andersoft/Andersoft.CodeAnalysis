using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap057Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP057", "Task.WhenAny abandons an incomplete operation",
        "Task '{0}' participates in WhenAny but is recreated before it is awaited — retain or cancel the incomplete operation",
        "Reliability", DiagnosticSeverity.Error, isEnabledByDefault: true);
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);
    public override void Initialize(AnalysisContext context) => ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP057");
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeAbandonedWhenAnyTask(SyntaxNodeAnalysisContext context)
    {
        if (!TryGetPresentationMethod(context, out var method)) return;
        foreach (var loop in method.DescendantNodes().OfType<WhileStatementSyntax>())
        {
            var declarations = loop.Statement.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                .Where(declaration => declaration.Initializer is not null)
                .ToDictionary(declaration => declaration.Identifier.ValueText);
            foreach (var whenAny in loop.Statement.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(IsTaskWhenAny))
            {
                var taskNames = whenAny.ArgumentList.Arguments.Select(argument => argument.Expression)
                    .OfType<IdentifierNameSyntax>().Select(identifier => identifier.Identifier.ValueText).ToArray();
                var winningTask = GetComparedWinningTask(whenAny);
                foreach (var taskName in taskNames)
                {
                    if (!declarations.TryGetValue(taskName, out var declaration)) continue;
                    if (taskName == winningTask) continue;
                    if (winningTask is not null && taskName != winningTask &&
                        whenAny.FirstAncestorOrSelf<IfStatementSyntax>() is { Statement: var winningBranch } &&
                        winningBranch.DescendantNodesAndSelf().OfType<ContinueStatementSyntax>().Any())
                    {
                        Report(context, Diagnostic.Create(Rap057Analyzer.Rule, declaration.Identifier.GetLocation(), taskName));
                        continue;
                    }

                    var awaitedAfterRace = loop.Statement.DescendantNodes().OfType<AwaitExpressionSyntax>()
                        .Any(awaitExpression => awaitExpression.SpanStart > whenAny.Span.End && awaitExpression.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == taskName);
                    if (awaitedAfterRace) continue;
                    Report(context, Diagnostic.Create(Rap057Analyzer.Rule, declaration.Identifier.GetLocation(), taskName));
                }
            }
        }
    }

    private static bool IsTaskWhenAny(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "WhenAny" } member &&
        member.Expression.ToString().EndsWith("Task", System.StringComparison.Ordinal);

    private static string? GetComparedWinningTask(InvocationExpressionSyntax whenAny)
    {
        var awaitExpression = whenAny.Parent as AwaitExpressionSyntax;
        var comparison = awaitExpression?.Parent as BinaryExpressionSyntax;
        if (comparison is null) return null;
        var other = comparison.Left == awaitExpression ? comparison.Right : comparison.Left;
        return other is IdentifierNameSyntax identifier ? identifier.Identifier.ValueText : null;
    }
}
