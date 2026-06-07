using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap047Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP047",
        "E2E test must not call the API directly",
        "E2E test '{0}' makes a direct API call; use Playwright UI interactions instead",
        "Test Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(startContext =>
        {
            if (!IsTestCompilation(startContext.Compilation))
                return;

            var httpClientSymbol = startContext.Compilation.GetTypeByMetadataName("System.Net.Http.HttpClient");
            var httpClientHandlerSymbol = startContext.Compilation.GetTypeByMetadataName("System.Net.Http.HttpClientHandler");

            startContext.RegisterSyntaxNodeAction(ctx =>
            {
                var invocation = (InvocationExpressionSyntax)ctx.Node;
                AnalyzeInvocation(ctx, invocation, httpClientSymbol, httpClientHandlerSymbol);
            }, SyntaxKind.InvocationExpression);

            startContext.RegisterSyntaxNodeAction(ctx =>
            {
                var creation = (ObjectCreationExpressionSyntax)ctx.Node;
                AnalyzeObjectCreation(ctx, creation, httpClientSymbol, httpClientHandlerSymbol);
            }, SyntaxKind.ObjectCreationExpression);
        });
    }

    private static bool IsTestCompilation(Compilation compilation)
    {
        var testAssemblies = new[]
        {
            "xunit.core",
            "Microsoft.Playwright",
            "nunit.framework",
            "Microsoft.VisualStudio.TestPlatform.TestFramework",
        };

        foreach (var reference in compilation.ReferencedAssemblyNames)
        {
            foreach (var test in testAssemblies)
            {
                if (reference.Name.StartsWith(test, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static bool IsHttpClientType(ITypeSymbol? type, INamedTypeSymbol? httpClientSymbol, INamedTypeSymbol? httpClientHandlerSymbol)
    {
        if (type is null)
            return false;

        if (httpClientSymbol is not null && SymbolEqualityComparer.Default.Equals(type, httpClientSymbol))
            return true;

        if (httpClientHandlerSymbol is not null && SymbolEqualityComparer.Default.Equals(type, httpClientHandlerSymbol))
            return true;

        return false;
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol? httpClientSymbol,
        INamedTypeSymbol? httpClientHandlerSymbol)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var memberSymbol = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol;
        if (memberSymbol is null)
            return;

        var containingType = context.ContainingSymbol?.ContainingType;
        if (containingType is null)
            return;

        var memberContainingType = memberSymbol.ContainingType;

        if (IsHttpClientType(memberContainingType, httpClientSymbol, httpClientHandlerSymbol))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), containingType.Name));
        }
    }

    private static void AnalyzeObjectCreation(
        SyntaxNodeAnalysisContext context,
        ObjectCreationExpressionSyntax creation,
        INamedTypeSymbol? httpClientSymbol,
        INamedTypeSymbol? httpClientHandlerSymbol)
    {
        var typeSymbol = context.SemanticModel.GetSymbolInfo(creation.Type, context.CancellationToken).Symbol as INamedTypeSymbol;
        if (typeSymbol is null)
            return;

        if (!IsHttpClientType(typeSymbol, httpClientSymbol, httpClientHandlerSymbol))
            return;

        var containingType = context.ContainingSymbol?.ContainingType;
        if (containingType is null)
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, creation.GetLocation(), containingType.Name));
    }
}
