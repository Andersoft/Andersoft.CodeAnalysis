using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;

namespace Andersoft.CodeAnalysis.EntityFramework;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap029Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP029",
        "Unbounded EF Core query — missing filter or limit",
        "Unbounded EF Core query: {0} materializes all rows without .Where(), .Take(), or .First*(). Refactor to use pagination techniques (e.g. .Skip()/.Take() with a page size, or keyset pagination) so the query never materializes the full table",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        EfAnalyzer.InitializeRule(context, "RAP029");
    }
}

internal static partial class EfAnalyzer
{
    private static readonly string[] FullMaterializers =
    {
        "ToList", "ToListAsync",
        "ToArray", "ToArrayAsync",
        "ToHashSet", "ToHashSetAsync",
        "ToDictionary", "ToDictionaryAsync",
    };

    private static readonly string[] BoundingMethods =
    {
        "Where",
        "Take", "Skip",
        "First", "FirstOrDefault", "FirstAsync", "FirstOrDefaultAsync",
        "Single", "SingleOrDefault", "SingleAsync", "SingleOrDefaultAsync",
        "Last", "LastOrDefault", "LastAsync", "LastOrDefaultAsync",
        "Min", "MinBy", "MinAsync", "MinByAsync",
        "Max", "MaxBy", "MaxAsync", "MaxByAsync",
    };

    private static void AnalyzeUnboundedQuery(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
        {
            return;
        }

        if (IsGeneratedFile(invocation.SyntaxTree.FilePath))
        {
            return;
        }

        if (context.SemanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol)
        {
            return;
        }

        if (!FullMaterializers.Contains(methodSymbol.Name, StringComparer.Ordinal))
        {
            return;
        }

        if (!IsQueryableOrDbSetReceiver(invocation, methodSymbol, context.SemanticModel))
        {
            return;
        }

        var receiver = GetReceiverExpression(invocation);
        if (receiver is null)
        {
            return;
        }

        if (ChainContainsBoundingMethod(receiver))
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap029Analyzer.Rule,
            invocation.GetLocation(),
            methodSymbol.Name));
    }

    private static ExpressionSyntax? GetReceiverExpression(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Expression;
        }

        return null;
    }

    private static bool ChainContainsBoundingMethod(ExpressionSyntax expression)
    {
        var current = expression;

        while (current is InvocationExpressionSyntax innerInvocation)
        {
            if (innerInvocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                var methodName = memberAccess.Name.Identifier.ValueText;

                if (BoundingMethods.Contains(methodName, StringComparer.Ordinal))
                {
                    return true;
                }

                current = memberAccess.Expression;
            }
            else
            {
                break;
            }
        }

        return false;
    }

    private static bool IsQueryableOrDbSetReceiver(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel)
    {
        if (methodSymbol.IsExtensionMethod && methodSymbol.ReceiverType is not null)
        {
            return IsQueryableOrDbSetType(methodSymbol.ReceiverType);
        }

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
            return IsQueryableOrDbSetType(receiverType);
        }

        return false;
    }
}
