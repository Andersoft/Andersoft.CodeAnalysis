using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
            var parameterClumps = new ConcurrentDictionary<string, ConcurrentBag<(IMethodSymbol Method, Location Location)>>();
            var enumSwitchSites = new ConcurrentDictionary<INamedTypeSymbol, ConcurrentBag<(INamedTypeSymbol? Container, Location Location)>>(SymbolEqualityComparer.Default);

            startContext.RegisterSyntaxNodeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeUsingDirective(ctx)), SyntaxKind.UsingDirective);
            startContext.RegisterSyntaxNodeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeNowMemberAccess(ctx)), SyntaxKind.SimpleMemberAccessExpression);
            startContext.RegisterSyntaxNodeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeMethodDeclaration(ctx)), SyntaxKind.MethodDeclaration);
            startContext.RegisterSyntaxNodeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeCatchClause(ctx)), SyntaxKind.CatchClause);
            startContext.RegisterSyntaxNodeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeNullableContractsAndState(ctx)), SyntaxKind.NullableType);
            startContext.RegisterSyntaxNodeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeReturnTypeContract(ctx)), SyntaxKind.MethodDeclaration, SyntaxKind.LocalFunctionStatement);
            startContext.RegisterSyntaxNodeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeStronglyTypedIdMembers(ctx)), SyntaxKind.PropertyDeclaration, SyntaxKind.FieldDeclaration);
            startContext.RegisterSyntaxTreeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeFileName(ctx)));

            startContext.RegisterSymbolAction(
                ctx => ExecuteForRule(ruleId, () => AnalyzeNamedType(ctx)),
                SymbolKind.NamedType);
            switch (ruleId)
            {
                case "RAP031": RegisterMemberAction(startContext, ruleId, AnalyzeCyclomaticComplexity); break;
                case "RAP032": RegisterMemberAction(startContext, ruleId, AnalyzeCognitiveComplexity); break;
                case "RAP033": RegisterMemberAction(startContext, ruleId, AnalyzeHalsteadComplexity); break;
                case "RAP034": RegisterMemberAction(startContext, ruleId, AnalyzeMaintainabilityIndex); break;
                case "RAP035": RegisterMemberAction(startContext, ruleId, AnalyzeNPathComplexity); break;
                case "RAP036":
                    startContext.RegisterSyntaxNodeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeLongParameterList(ctx)), SyntaxKind.MethodDeclaration, SyntaxKind.ConstructorDeclaration, SyntaxKind.LocalFunctionStatement, SyntaxKind.DelegateDeclaration);
                    break;
                case "RAP037": RegisterClassAction(startContext, ruleId, AnalyzeLargeClass); break;
                case "RAP068": RegisterClassAction(startContext, ruleId, AnalyzeLargeAggregate); break;
                case "RAP039":
                    startContext.RegisterSymbolAction(ctx => ExecuteForRule(ruleId, () => CollectParameterClumps(ctx, parameterClumps)), SymbolKind.Method);
                    startContext.RegisterCompilationEndAction(ctx => ExecuteForRule(ruleId, () => AnalyzeRap039DataClumps(ctx, parameterClumps)));
                    break;
                case "RAP040":
                    startContext.RegisterSyntaxNodeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeMessageChain(ctx)), SyntaxKind.SimpleMemberAccessExpression);
                    break;
                case "RAP041": RegisterClassAction(startContext, ruleId, AnalyzeTemporaryField); break;
                case "RAP042":
                    startContext.RegisterSyntaxNodeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeFeatureEnvy(ctx)), SyntaxKind.MethodDeclaration);
                    break;
                case "RAP043":
                    startContext.RegisterSyntaxNodeAction(ctx => ExecuteForRule(ruleId, () => CollectEnumSwitchSites(ctx, enumSwitchSites)), SyntaxKind.SwitchStatement, SyntaxKind.SwitchExpression);
                    startContext.RegisterCompilationEndAction(ctx => ExecuteForRule(ruleId, () => AnalyzeRap043RepeatedSwitches(ctx, enumSwitchSites)));
                    break;
                case "RAP044": RegisterClassAction(startContext, ruleId, AnalyzeMiddleMan); break;
                case "RAP045": RegisterClassAction(startContext, ruleId, AnalyzeLazyClass); break;
                case "RAP046": RegisterClassAction(startContext, ruleId, AnalyzeAnemicDomainClass); break;
                case "RAP049": RegisterClassAction(startContext, ruleId, AnalyzeEndpointOperationCount); break;
                case "RAP050":
                    startContext.RegisterSyntaxNodeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeHardCodedRole(ctx)), SyntaxKind.InvocationExpression);
                    break;
                case "RAP051":
                    startContext.RegisterSyntaxNodeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeCatchAllResultMapping(ctx)), SyntaxKind.SwitchExpression);
                    break;
                case "RAP052":
                    startContext.RegisterSyntaxNodeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeManualServerSentEvents(ctx)), SyntaxKind.MethodDeclaration);
                    break;
                case "RAP053": RegisterPresentationMethodAction(startContext, ruleId, AnalyzeCreatedLocation); break;
                case "RAP054": RegisterPresentationMethodAction(startContext, ruleId, AnalyzeDuplicateCurrentUserInputs); break;
                case "RAP055": RegisterPresentationMethodAction(startContext, ruleId, AnalyzeUntypedEndpointResult); break;
                case "RAP056": RegisterPresentationMethodAction(startContext, ruleId, AnalyzeBooleanHttpMappingParameter); break;
                case "RAP057": RegisterPresentationMethodAction(startContext, ruleId, AnalyzeAbandonedWhenAnyTask); break;
                case "RAP058": RegisterPresentationMethodAction(startContext, ruleId, AnalyzeManualJsonTextResponse); break;
                case "RAP059": RegisterPresentationMethodAction(startContext, ruleId, AnalyzePresentationTimestampRepair); break;
                case "RAP060": RegisterPresentationMethodAction(startContext, ruleId, AnalyzeHandlerLocalAuthorization); break;
                case "RAP061":
                    startContext.RegisterSymbolAction(ctx => ExecuteForRule(ruleId, () => AnalyzeMessageHandlerReturnType(ctx)), SymbolKind.NamedType);
                    break;
                case "RAP026":
                    startContext.RegisterSyntaxNodeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeRap026NullForgivingWithoutJustification(
                        ctx,
                        (PostfixUnaryExpressionSyntax)ctx.Node,
                        GetContainingNamespace(ctx.Node))), SyntaxKind.SuppressNullableWarningExpression);
                    break;
                case "RAP028":
                    startContext.RegisterSyntaxNodeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeMissingCancellationToken(ctx)), SyntaxKind.InvocationExpression);
                    break;
                case "RAP030":
                    startContext.RegisterSyntaxNodeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeDefaultCancellationToken(ctx)), SyntaxKind.Parameter);
                    break;
                case "RAP062":
                    startContext.RegisterSyntaxNodeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeIfStatementBody(ctx)), SyntaxKind.IfStatement);
                    startContext.RegisterSyntaxNodeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeExpressionBodiedMethod(ctx)), SyntaxKind.MethodDeclaration);
                    startContext.RegisterSyntaxNodeAction(
                        ctx => ExecuteForRule(ruleId, () => AnalyzeExpressionBodiedLambda(ctx)),
                        SyntaxKind.SimpleLambdaExpression,
                        SyntaxKind.ParenthesizedLambdaExpression);
                    startContext.RegisterSyntaxTreeAction(ctx => ExecuteForRule(ruleId, () => AnalyzeBracePlacement(ctx)));
                    break;
                case "RAP063":
                    startContext.RegisterSyntaxNodeAction(
                        ctx => ExecuteForRule(ruleId, () => AnalyzeJsonDomContract(ctx)),
                        SyntaxKind.Parameter,
                        SyntaxKind.MethodDeclaration,
                        SyntaxKind.LocalFunctionStatement,
                        SyntaxKind.DelegateDeclaration,
                        SyntaxKind.PropertyDeclaration,
                        SyntaxKind.IndexerDeclaration,
                        SyntaxKind.FieldDeclaration,
                        SyntaxKind.EventFieldDeclaration,
                        SyntaxKind.EventDeclaration,
                        SyntaxKind.OperatorDeclaration,
                        SyntaxKind.ConversionOperatorDeclaration);
                    break;
                case "RAP064":
                    startContext.RegisterSyntaxNodeAction(
                        ctx => ExecuteForRule(ruleId, () => AnalyzeMessageHandlerDependency(ctx)),
                        SyntaxKind.Parameter,
                        SyntaxKind.PropertyDeclaration,
                        SyntaxKind.FieldDeclaration);
                    break;
            }
        });
    }

    private static void RegisterMemberAction(
        CompilationStartAnalysisContext context,
        string ruleId,
        Action<SyntaxNodeAnalysisContext> action) =>
        context.RegisterSyntaxNodeAction(
            ctx => ExecuteForRule(ruleId, () => action(ctx)),
            SyntaxKind.MethodDeclaration,
            SyntaxKind.ConstructorDeclaration,
            SyntaxKind.LocalFunctionStatement,
            SyntaxKind.GetAccessorDeclaration,
            SyntaxKind.SetAccessorDeclaration,
            SyntaxKind.InitAccessorDeclaration);

    private static void RegisterClassAction(
        CompilationStartAnalysisContext context,
        string ruleId,
        Action<SyntaxNodeAnalysisContext> action) =>
        context.RegisterSyntaxNodeAction(
            ctx => ExecuteForRule(ruleId, () => action(ctx)),
            SyntaxKind.ClassDeclaration);

    private static void RegisterPresentationMethodAction(
        CompilationStartAnalysisContext context,
        string ruleId,
        Action<SyntaxNodeAnalysisContext> action) =>
        context.RegisterSyntaxNodeAction(
            ctx => ExecuteForRule(ruleId, () => action(ctx)),
            SyntaxKind.MethodDeclaration);
}
