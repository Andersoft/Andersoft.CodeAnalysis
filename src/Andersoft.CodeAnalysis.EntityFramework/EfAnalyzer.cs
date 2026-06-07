using System;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Andersoft.CodeAnalysis.EntityFramework;

internal static partial class EfAnalyzer
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

    internal static void InitializeRule(AnalysisContext context, string ruleId)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(startContext =>
        {
            var dbContextSymbol = startContext.Compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext");
            var migrationSymbol = startContext.Compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.Migrations.Migration");

            startContext.RegisterSymbolAction(
                ctx => ExecuteForRule(ruleId, () => AnalyzeNamedType(ctx, dbContextSymbol, migrationSymbol)),
                SymbolKind.NamedType);

            startContext.RegisterSyntaxNodeAction(
                ctx => ExecuteForRule(ruleId, () => AnalyzeMethodDeclarationForPresentation(ctx)),
                SyntaxKind.MethodDeclaration);

            startContext.RegisterSyntaxNodeAction(
                ctx => ExecuteForRule(ruleId, () => AnalyzeClassDeclarationForConcurrency(ctx)),
                SyntaxKind.ClassDeclaration);

            startContext.RegisterSyntaxNodeAction(
                ctx => ExecuteForRule(ruleId, () => AnalyzeLoopQuery(ctx)),
                SyntaxKind.ForEachStatement,
                SyntaxKind.ForStatement,
                SyntaxKind.WhileStatement,
                SyntaxKind.DoStatement);

            startContext.RegisterSyntaxNodeAction(
                ctx => ExecuteForRule(ruleId, () => AnalyzeUnboundedQuery(ctx)),
                SyntaxKind.InvocationExpression);
        });
    }

    private static void AnalyzeNamedType(
        SymbolAnalysisContext context,
        INamedTypeSymbol? dbContextSymbol,
        INamedTypeSymbol? migrationSymbol)
    {
        if (context.Symbol is not INamedTypeSymbol namedType)
        {
            return;
        }

        if (namedType.Locations.IsDefaultOrEmpty || namedType.Locations[0].SourceTree is null)
        {
            return;
        }

        var filePath = namedType.Locations[0].SourceTree!.FilePath;
        if (IsGeneratedFile(filePath))
        {
            return;
        }

        AnalyzeRap010PresentationContractBoundary(context, namedType, dbContextSymbol);

        if (migrationSymbol is not null)
        {
            AnalyzeRap011MigrationPlacement(context, namedType, filePath, migrationSymbol);
        }

        var namespaceText = namedType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (!namespaceText.Contains(".Presentation.", StringComparison.Ordinal))
        {
            return;
        }

        if (!namedType.Name.EndsWith("Controller", StringComparison.Ordinal) &&
            !namedType.Name.EndsWith("Hub", StringComparison.Ordinal))
        {
            return;
        }

        AnalyzeRap002PresentationDbContextDependency(context, namedType, dbContextSymbol);
    }

    private static void AnalyzeMethodDeclarationForPresentation(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not MethodDeclarationSyntax methodDeclaration)
        {
            return;
        }

        if (IsGeneratedFile(methodDeclaration.SyntaxTree.FilePath))
        {
            return;
        }

        var containingNamespace = GetContainingNamespace(methodDeclaration);
        if (!containingNamespace.Contains(".Presentation.", StringComparison.Ordinal))
        {
            return;
        }

        if (context.SemanticModel.GetDeclaredSymbol(methodDeclaration) is not IMethodSymbol methodSymbol)
        {
            return;
        }

        AnalyzeRap014PresentationBusinessLogic(context, methodDeclaration, methodSymbol);
    }

    private static void AnalyzeClassDeclarationForConcurrency(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not ClassDeclarationSyntax classDeclaration)
        {
            return;
        }

        if (IsGeneratedFile(classDeclaration.SyntaxTree.FilePath))
        {
            return;
        }

        var declaredSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);
        if (declaredSymbol is not INamedTypeSymbol namedType)
        {
            return;
        }

        var entityConfigurationTypeSymbol = context.Compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.IEntityTypeConfiguration`1");
        AnalyzeRap025MissingConcurrencyToken(context, classDeclaration, namedType, entityConfigurationTypeSymbol);
    }

    private static bool IsPresentationControllerOrHub(INamedTypeSymbol namedType)
    {
        var namespaceText = namedType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return namespaceText.Contains(".Presentation.", StringComparison.Ordinal) &&
            (namedType.Name.EndsWith("Controller", StringComparison.Ordinal) ||
             namedType.Name.EndsWith("Hub", StringComparison.Ordinal));
    }

    private static bool IsPresentationContractNamespace(string namespaceText) =>
        namespaceText.Contains(".Presentation.Contracts", StringComparison.Ordinal);

    private static bool IsForbiddenPresentationContractType(ITypeSymbol typeSymbol, INamedTypeSymbol? dbContextSymbol)
    {
        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            return IsForbiddenPresentationContractType(arrayType.ElementType, dbContextSymbol);
        }

        if (typeSymbol is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var ns = namedType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (ns.Contains(".Domain.", StringComparison.Ordinal))
        {
            return true;
        }

        if (dbContextSymbol is not null && InheritsOrImplements(namedType, dbContextSymbol))
        {
            return true;
        }

        if (ns.Contains(".Infrastructure.", StringComparison.Ordinal) &&
            (namedType.Name.EndsWith("Entity", StringComparison.Ordinal) ||
             namedType.Name.EndsWith("Model", StringComparison.Ordinal)))
        {
            return true;
        }

        foreach (var typeArgument in namedType.TypeArguments)
        {
            if (IsForbiddenPresentationContractType(typeArgument, dbContextSymbol))
            {
                return true;
            }
        }

        return false;
    }

    private static bool InheritsOrImplements(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        var current = type;
        while (current is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }

            current = current.BaseType;
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDomainOrInfrastructureType(INamedTypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        var ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return ns.Contains(".Domain.", StringComparison.Ordinal) ||
            ns.Contains(".Infrastructure.", StringComparison.Ordinal);
    }

    private static string GetContainingNamespace(SyntaxNode node)
    {
        var current = node;
        while (current is not null)
        {
            if (current is BaseNamespaceDeclarationSyntax ns)
            {
                return ns.Name.ToString();
            }

            current = current.Parent;
        }

        return string.Empty;
    }

    private static bool IsGeneratedFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return true;
        }

        var normalized = filePath.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase);
    }
}
