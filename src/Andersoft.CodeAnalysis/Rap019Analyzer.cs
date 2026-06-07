using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap019Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP019",
        "List endpoint missing pagination guard",
        "List endpoint missing pagination guard",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP019");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeRap019ListEndpointPagination(
        SyntaxNodeAnalysisContext context,
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol)
    {
        if (!AnalyzeRap019LooksLikeListReturn(methodSymbol.ReturnType))
        {
            return;
        }

        var hasPaginationParameter = methodSymbol.Parameters.Any(static p =>
            p.Name.Equals("page", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals("pageNumber", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals("pageSize", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals("limit", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals("offset", StringComparison.OrdinalIgnoreCase));

        var enforcesLimit = methodDeclaration.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(inv => context.SemanticModel.GetSymbolInfo(inv).Symbol as IMethodSymbol)
            .Any(static s => s is not null && (s.Name == "Take" || s.Name == "Skip"));

        if (hasPaginationParameter || enforcesLimit)
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap019Analyzer.Rule,
            methodDeclaration.Identifier.GetLocation(),
            methodSymbol.Name));
    }

    private static bool AnalyzeRap019LooksLikeListReturn(ITypeSymbol returnType)
    {
        if (returnType is not INamedTypeSymbol named)
        {
            return false;
        }

        if (named.IsGenericType && named.TypeArguments.Length == 1)
        {
            var constructed = named.ConstructedFrom.ToDisplayString();
            if (constructed is "System.Collections.Generic.IEnumerable<T>" or
                "System.Collections.Generic.IReadOnlyList<T>" or
                "System.Collections.Generic.List<T>" or
                "System.Threading.Tasks.Task<TResult>" or
                "System.Threading.Tasks.ValueTask<TResult>" or
                "Microsoft.AspNetCore.Mvc.ActionResult<T>")
            {
                return true;
            }
        }

        var display = named.ToDisplayString();
        return display.Contains("IEnumerable<", StringComparison.Ordinal) ||
            display.Contains("IReadOnlyList<", StringComparison.Ordinal) ||
            display.Contains("List<", StringComparison.Ordinal);
    }
}
