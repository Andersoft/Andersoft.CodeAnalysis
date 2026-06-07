using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;

namespace Andersoft.CodeAnalysis.EntityFramework;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap027Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP027",
        "EF Core query inside loop may cause N+1 queries",
        "EF Core query inside loop may cause N+1 queries. Use .Include() or batch loading instead",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        EfAnalyzer.InitializeRule(context, "RAP027");
    }
}

internal static partial class EfAnalyzer
{
    private static readonly string[] EfCoreMaterializers =
    {
        "ToListAsync", "ToList",
        "FirstOrDefaultAsync", "FirstOrDefault",
        "SingleOrDefaultAsync", "SingleOrDefault",
        "FirstAsync", "First",
        "SingleAsync", "Single",
        "LastOrDefaultAsync", "LastOrDefault",
        "LastAsync", "Last",
        "CountAsync", "Count",
        "AnyAsync", "Any",
        "ToArrayAsync", "ToArray",
        "ToDictionaryAsync", "ToDictionary",
        "ToHashSetAsync", "ToHashSet",
        "FindAsync", "Find",
        "LongCountAsync",
        "MinAsync", "MaxAsync",
        "SumAsync", "AverageAsync",
    };

    private static void AnalyzeLoopQuery(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not StatementSyntax loopStatement)
        {
            return;
        }

        if (IsGeneratedFile(loopStatement.SyntaxTree.FilePath))
        {
            return;
        }

        SyntaxNode? loopBody = loopStatement switch
        {
            ForEachStatementSyntax forEach => forEach.Statement,
            ForStatementSyntax forStmt => forStmt.Statement,
            WhileStatementSyntax whileStmt => whileStmt.Statement,
            DoStatementSyntax doStmt => doStmt.Statement,
            _ => null,
        };

        if (loopBody is null)
        {
            return;
        }

        foreach (var invocation in loopBody.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (HasEnclosingLoop(invocation, loopStatement))
            {
                continue;
            }

            if (context.SemanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol)
            {
                continue;
            }

            if (!EfCoreMaterializers.Contains(methodSymbol.Name, StringComparer.Ordinal))
            {
                continue;
            }

            if (!IsQueryableReceiver(methodSymbol, context.SemanticModel, invocation))
            {
                continue;
            }

            Report(context, Diagnostic.Create(
                Rap027Analyzer.Rule,
                invocation.GetLocation()));
        }
    }

    private static bool HasEnclosingLoop(SyntaxNode invocation, StatementSyntax outerLoop)
    {
        foreach (var ancestor in invocation.Ancestors())
        {
            if (ancestor == outerLoop)
            {
                return false;
            }

            if (ancestor is ForEachStatementSyntax or ForStatementSyntax
                or WhileStatementSyntax or DoStatementSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsQueryableReceiver(
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation)
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

    private static bool IsQueryableOrDbSetType(ITypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        var typeName = type.ToDisplayString();

        if (typeName.StartsWith("System.Linq.IQueryable", StringComparison.Ordinal))
        {
            return true;
        }

        if (typeName.Contains("Microsoft.EntityFrameworkCore.DbSet", StringComparison.Ordinal))
        {
            return true;
        }

        if (typeName.Contains("Microsoft.EntityFrameworkCore.DbContext", StringComparison.Ordinal) ||
            typeName.EndsWith("DbContext", StringComparison.Ordinal))
        {
            return true;
        }

        if (type is INamedTypeSymbol namedType)
        {
            foreach (var iface in namedType.AllInterfaces)
            {
                var ifaceName = iface.ToDisplayString();
                if (ifaceName.StartsWith("System.Linq.IQueryable", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
