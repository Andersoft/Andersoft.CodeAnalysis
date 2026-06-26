using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Andersoft.CodeAnalysis;

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeUsingDirective(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not UsingDirectiveSyntax usingDirective)
        {
            return;
        }

        if (IsGeneratedFile(usingDirective.SyntaxTree.FilePath))
        {
            return;
        }

        var sourceNamespace = GetContainingNamespace(usingDirective);
        if (string.IsNullOrWhiteSpace(sourceNamespace))
        {
            return;
        }

        var targetNamespace = usingDirective.Name?.ToString();
        if (string.IsNullOrWhiteSpace(targetNamespace))
        {
            return;
        }

        var nonNullTargetNamespace = targetNamespace!;
        AnalyzeRap001LayerDependency(context, usingDirective, sourceNamespace, nonNullTargetNamespace);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
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

        AnalyzeRap022FeatureFlagMetadata(context, namedType);

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

        AnalyzeRap012PresentationDependency(context, namedType);
    }

    private static void AnalyzeNowMemberAccess(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        if (IsGeneratedFile(memberAccess.SyntaxTree.FilePath))
        {
            return;
        }

        var containingNamespace = GetContainingNamespace(memberAccess);

        var symbol = context.SemanticModel.GetSymbolInfo(memberAccess).Symbol;
        if (symbol is not IPropertySymbol propertySymbol)
        {
            return;
        }

        var containingType = propertySymbol.ContainingType?.ToDisplayString();
        var propertyName = propertySymbol.Name;
        if ((containingType is "System.DateTime" or "System.DateTimeOffset") &&
            (propertyName is "Now" or "UtcNow"))
        {
            var layer = GetLayerLabel(containingNamespace);
            AnalyzeRap003SystemClockUsage(context, memberAccess, containingType!, propertyName, layer);
            AnalyzeRap017GlobalNowUsage(context, memberAccess, containingNamespace, containingType!, propertyName);
        }
    }

    private static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
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

        if (context.SemanticModel.GetDeclaredSymbol(methodDeclaration) is not IMethodSymbol methodSymbol)
        {
            return;
        }

        if (methodSymbol.MethodKind != MethodKind.Ordinary ||
            methodSymbol.DeclaredAccessibility != Accessibility.Public ||
            methodSymbol.IsOverride)
        {
            return;
        }

        if (ReturnsTaskLike(methodSymbol) && !HasCancellationToken(methodSymbol))
        {
            AnalyzeRap004MissingCancellationToken(context, methodDeclaration, methodSymbol);
        }

        foreach (var parameter in methodSymbol.Parameters)
        {
            if (!parameter.Name.EndsWith("id", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var typeName = parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            if (IsPrimitiveIdType(parameter.Type))
            {
                AnalyzeRap005PrimitiveIdParameter(context, parameter, typeName);
            }
        }

        if (containingNamespace.Contains(".Presentation.", StringComparison.Ordinal) &&
            context.ContainingSymbol?.ContainingType is INamedTypeSymbol presentationType &&
            IsPresentationControllerOrHub(presentationType))
        {
            AnalyzeRap019ListEndpointPagination(context, methodDeclaration, methodSymbol);
            AnalyzeRap020ErrorCodePresence(context, methodDeclaration, methodSymbol);
        }

        AnalyzeRap016CancellationTokenPropagation(context, methodDeclaration, methodSymbol);
        AnalyzeRap021SensitiveLogging(context, methodDeclaration, methodSymbol);
    }

    private static void AnalyzeCatchClause(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not CatchClauseSyntax catchClause)
        {
            return;
        }

        if (IsGeneratedFile(catchClause.SyntaxTree.FilePath))
        {
            return;
        }

        if (catchClause.Declaration?.Type is null)
        {
            return;
        }

        var containingType = context.ContainingSymbol?.ContainingType;
        if (containingType is null || !IsPresentationControllerOrHub(containingType))
        {
            return;
        }

        if (catchClause.FirstAncestorOrSelf<MethodDeclarationSyntax>() is not MethodDeclarationSyntax methodDeclaration)
        {
            return;
        }

        if (!MethodUsesOneOfDispatch(methodDeclaration, context.SemanticModel))
        {
            return;
        }

        var caughtType = context.SemanticModel.GetTypeInfo(catchClause.Declaration.Type).Type as INamedTypeSymbol;
        if (!IsBusinessExceptionType(caughtType))
        {
            return;
        }

        AnalyzeRap015PresentationTryCatchExpectedOutcome(context, catchClause, methodDeclaration, caughtType!);
    }

    private static void AnalyzeStronglyTypedIdMembers(SyntaxNodeAnalysisContext context)
    {
        if (IsGeneratedFile(context.Node.SyntaxTree.FilePath))
        {
            return;
        }

        var containingNamespace = GetContainingNamespace(context.Node);
        AnalyzeRap024StronglyTypedIdMembers(context, containingNamespace);
    }

    private static void AnalyzeFileName(SyntaxTreeAnalysisContext context)
    {
        var filePath = context.Tree.FilePath;
        if (string.IsNullOrWhiteSpace(filePath) || IsGeneratedFile(filePath))
        {
            return;
        }

        var fileName = Path.GetFileName(filePath);
        if (!fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        AnalyzeRap006BannedFileName(context, fileName);
    }

    private static void AnalyzeReturnTypeContract(SyntaxNodeAnalysisContext context)
    {
        if (IsGeneratedFile(context.Node.SyntaxTree.FilePath))
        {
            return;
        }

        TypeSyntax returnType;
        string ownerName;
        switch (context.Node)
        {
            case MethodDeclarationSyntax method:
                returnType = method.ReturnType;
                ownerName = method.Identifier.ValueText;
                break;
            case LocalFunctionStatementSyntax localFunction:
                returnType = localFunction.ReturnType;
                ownerName = localFunction.Identifier.ValueText;
                break;
            default:
                return;
        }

        AnalyzeRap013OptionalReturnType(context, returnType, ownerName);
    }

    private static void AnalyzeNullableContractsAndState(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not NullableTypeSyntax nullableType)
        {
            return;
        }

        if (IsGeneratedFile(nullableType.SyntaxTree.FilePath))
        {
            return;
        }

        if (!TryDescribeNullableContractTarget(nullableType, out var targetKind, out var targetName))
        {
            return;
        }

        // Use the syntax node directly so the displayed type always keeps its '?'
        // annotation. The semantic display string drops '?' on reference types in
        // a nullable-enabled context (e.g. 'DbContext' instead of 'DbContext?').
        var displayType = nullableType.ToString();
        AnalyzeRap013NullableContractsAndState(context, nullableType, targetKind, targetName, displayType);
    }
}
