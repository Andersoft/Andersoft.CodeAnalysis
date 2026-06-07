using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Andersoft.CodeAnalysis;

internal static partial class ArchitectureConventionsAnalyzer
{
    private static readonly AsyncLocal<string?> ActiveRuleId = new();

    private static bool IsActive(string ruleId) =>
        string.Equals(ActiveRuleId.Value, ruleId, StringComparison.Ordinal);

    private static void ExecuteForRule(string ruleId, Action action)
    {
        var previous = ActiveRuleId.Value;
        ActiveRuleId.Value = ruleId;
        try
        {
            action();
        }
        finally
        {
            ActiveRuleId.Value = previous;
        }
    }

    private static void Report(SyntaxNodeAnalysisContext context, Diagnostic diagnostic)
    {
        if (IsActive(diagnostic.Id))
        {
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static void Report(SymbolAnalysisContext context, Diagnostic diagnostic)
    {
        if (IsActive(diagnostic.Id))
        {
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static void Report(SyntaxTreeAnalysisContext context, Diagnostic diagnostic)
    {
        if (IsActive(diagnostic.Id))
        {
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static void Report(CompilationAnalysisContext context, Diagnostic diagnostic)
    {
        if (IsActive(diagnostic.Id))
        {
            context.ReportDiagnostic(diagnostic);
        }
    }

    internal static void InitializeRule(AnalysisContext context, string ruleId)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(startContext =>
        {
            startContext.RegisterSyntaxNodeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeUsingDirective(ctx)), SyntaxKind.UsingDirective);
            startContext.RegisterSyntaxNodeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeNowMemberAccess(ctx)), SyntaxKind.SimpleMemberAccessExpression);
            startContext.RegisterSyntaxNodeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeMethodDeclaration(ctx)), SyntaxKind.MethodDeclaration);
            startContext.RegisterSyntaxNodeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeCatchClause(ctx)), SyntaxKind.CatchClause);
            startContext.RegisterSyntaxNodeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeNullableContractsAndState(ctx)), SyntaxKind.NullableType);
            startContext.RegisterSyntaxNodeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeStronglyTypedIdMembers(ctx)), SyntaxKind.PropertyDeclaration, SyntaxKind.FieldDeclaration);
            startContext.RegisterSyntaxTreeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeFileName(ctx)));

            startContext.RegisterSymbolAction(
                ctx => ExecuteForRule(ruleId, () => AnalyzeNamedType(ctx)),
                SymbolKind.NamedType);
        });
    }
}
