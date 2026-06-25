using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Andersoft.CodeAnalysis;

internal static partial class ArchitectureConventionsAnalyzer
{
    // Layer label used purely for diagnostic messages. Rules no longer gate on
    // layer (applicability is driven by .editorconfig), so this must return a
    // sensible value for every layer rather than assuming Domain/Application.
    private static string GetLayerLabel(string namespaceText)
    {
        if (NamespaceHasSegment(namespaceText, "Domain"))
        {
            return "Domain";
        }

        if (NamespaceHasSegment(namespaceText, "Application"))
        {
            return "Application";
        }

        if (NamespaceHasSegment(namespaceText, "Infrastructure"))
        {
            return "Infrastructure";
        }

        if (NamespaceHasSegment(namespaceText, "Presentation"))
        {
            return "Presentation";
        }

        return "Unknown";
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

    private static bool TryGetLayerViolation(string sourceNamespace, string targetNamespace, out string sourceLayer)
    {
        sourceLayer = string.Empty;

        if (sourceNamespace.Contains(".Domain.", StringComparison.Ordinal))
        {
            sourceLayer = "Domain";
            if (targetNamespace.Contains(".Application.", StringComparison.Ordinal) ||
                targetNamespace.Contains(".Infrastructure.", StringComparison.Ordinal) ||
                targetNamespace.Contains(".Presentation.", StringComparison.Ordinal) ||
                targetNamespace.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
                targetNamespace.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }

        if (sourceNamespace.Contains(".Application.", StringComparison.Ordinal))
        {
            sourceLayer = "Application";
            if (targetNamespace.Contains(".Infrastructure.", StringComparison.Ordinal) ||
                targetNamespace.Contains(".Presentation.", StringComparison.Ordinal) ||
                targetNamespace.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
                targetNamespace.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }

        if (sourceNamespace.Contains(".Presentation.", StringComparison.Ordinal))
        {
            sourceLayer = "Presentation";
            return targetNamespace.Contains(".Infrastructure.", StringComparison.Ordinal);
        }

        return false;
    }

    private static bool IsPresentationControllerOrHub(INamedTypeSymbol namedType)
    {
        var namespaceText = namedType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return namespaceText.Contains(".Presentation.", StringComparison.Ordinal) &&
            (namedType.Name.EndsWith("Controller", StringComparison.Ordinal) ||
             namedType.Name.EndsWith("Hub", StringComparison.Ordinal));
    }

    private static bool IsApplicationHandlerType(
        INamedTypeSymbol namedType,
        INamedTypeSymbol? iCommandHandlerSymbol,
        INamedTypeSymbol? iQueryHandlerSymbol)
    {
        var namespaceText = namedType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (!namespaceText.Contains(".Application.", StringComparison.Ordinal) || !namedType.Name.EndsWith("Handler", StringComparison.Ordinal))
        {
            return false;
        }

        return ImplementsGenericInterface(namedType, iCommandHandlerSymbol) ||
            ImplementsGenericInterface(namedType, iQueryHandlerSymbol);
    }

    private static bool MethodUsesOneOfDispatch(MethodDeclarationSyntax methodDeclaration, SemanticModel semanticModel)
    {
        foreach (var invocation in methodDeclaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol)
            {
                continue;
            }

            if (!string.Equals(methodSymbol.Name, "DispatchAsync", StringComparison.Ordinal))
            {
                continue;
            }

            if (IsOneOfOrTaskLikeOneOf(methodSymbol.ReturnType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsOneOfOrTaskLikeOneOf(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        if (IsOneOfType(namedType))
        {
            return true;
        }

        var constructedFrom = namedType.ConstructedFrom.ToDisplayString();
        if (constructedFrom is "System.Threading.Tasks.Task<TResult>" or "System.Threading.Tasks.ValueTask<TResult>" &&
            namedType.TypeArguments.Length == 1)
        {
            return IsOneOfOrTaskLikeOneOf(namedType.TypeArguments[0]);
        }

        return false;
    }

    private static bool IsOneOfType(INamedTypeSymbol typeSymbol)
    {
        var containingNamespace = typeSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return containingNamespace == "OneOf" && typeSymbol.Name.StartsWith("OneOf", StringComparison.Ordinal);
    }

    private static bool IsBusinessExceptionType(INamedTypeSymbol? exceptionType)
    {
        if (exceptionType is null)
        {
            return false;
        }

        var exceptionBase = exceptionType;
        var isException = false;
        while (exceptionBase is not null)
        {
            if (exceptionBase.ToDisplayString() == "System.Exception")
            {
                isException = true;
                break;
            }

            exceptionBase = exceptionBase.BaseType;
        }

        if (!isException)
        {
            return false;
        }

        var ns = exceptionType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (ns.Contains(".Common.Application.Errors", StringComparison.Ordinal) ||
            ns.Contains(".Domain.Errors", StringComparison.Ordinal))
        {
            return true;
        }

        return exceptionType.Name.Contains("NotFound", StringComparison.OrdinalIgnoreCase) ||
            exceptionType.Name.Contains("Conflict", StringComparison.OrdinalIgnoreCase) ||
            exceptionType.Name.Contains("Validation", StringComparison.OrdinalIgnoreCase) ||
            exceptionType.Name.Contains("Business", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedPresentationDependency(INamedTypeSymbol dependencyType)
    {
        var ns = dependencyType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (ns.StartsWith("System", StringComparison.Ordinal) || ns.StartsWith("Microsoft", StringComparison.Ordinal))
        {
            return true;
        }

        if (ns.Contains(".Application.", StringComparison.Ordinal))
        {
            return true;
        }

        if (dependencyType.Name.Contains("Mapper", StringComparison.Ordinal))
        {
            return true;
        }

        return dependencyType.ToDisplayString() == "Api.Features.Common.Application.TypedDispatcher";
    }

    private static bool IsDisallowedPresentationDependency(INamedTypeSymbol dependencyType)
    {
        var ns = dependencyType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return NamespaceHasSegment(ns, "Infrastructure") ||
            NamespaceHasSegment(ns, "Domain") ||
            dependencyType.Name.EndsWith("Repository", StringComparison.Ordinal) ||
            dependencyType.Name.Contains("DbContext", StringComparison.Ordinal) ||
            dependencyType.Name.Contains("UnitOfWork", StringComparison.Ordinal) ||
            dependencyType.Name.EndsWith("Store", StringComparison.Ordinal);
    }

    private static bool NamespaceHasSegment(string namespaceText, string segment)
    {
        return namespaceText.Contains($".{segment}.", StringComparison.Ordinal) ||
            namespaceText.EndsWith($".{segment}", StringComparison.Ordinal);
    }

    private static bool ImplementsGenericInterface(INamedTypeSymbol namedType, INamedTypeSymbol? interfaceSymbol)
    {
        if (interfaceSymbol is null)
        {
            return false;
        }

        foreach (var iface in namedType.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface.ConstructedFrom, interfaceSymbol))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasCancellationToken(IMethodSymbol methodSymbol)
    {
        foreach (var parameter in methodSymbol.Parameters)
        {
            if (parameter.Type.ToDisplayString() == "System.Threading.CancellationToken")
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReturnsTaskLike(IMethodSymbol methodSymbol)
    {
        var returnType = methodSymbol.ReturnType;
        if (returnType is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var display = namedType.ConstructedFrom.ToDisplayString();
        return display is "System.Threading.Tasks.Task" or
            "System.Threading.Tasks.Task<TResult>" or
            "System.Threading.Tasks.ValueTask" or
            "System.Threading.Tasks.ValueTask<TResult>";
    }

    private static bool IsPrimitiveIdType(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is INamedTypeSymbol named &&
            named.IsGenericType &&
            named.ConstructedFrom.ToDisplayString() == "System.Nullable<T>" &&
            named.TypeArguments.Length == 1)
        {
            return IsPrimitiveIdType(named.TypeArguments[0]);
        }

        if (IsWeakCommonIdType(typeSymbol))
        {
            return true;
        }

        if (typeSymbol.SpecialType is SpecialType.System_Int32 or
            SpecialType.System_Int64 or
            SpecialType.System_String)
        {
            return true;
        }

        return typeSymbol.ToDisplayString() == "System.Guid";
    }

    private static bool IsWeakCommonIdType(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var namespaceText = namedType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (!string.Equals(namespaceText, "Api.Features.Common.Domain", StringComparison.Ordinal))
        {
            return false;
        }

        return namedType.Name is "IntId" or "GuidId" or "StringId" or "OptionalIntId" or "OptionalGuidId";
    }

    private static bool IsIdShapedMemberName(string memberName)
    {
        return memberName.EndsWith("Id", StringComparison.Ordinal) ||
            memberName.EndsWith("Ids", StringComparison.Ordinal) ||
            memberName.Contains("Id", StringComparison.Ordinal);
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

    private static bool TryDescribeNullableContractTarget(
        NullableTypeSyntax nullableType,
        out string targetKind,
        out string targetName,
        out string layer)
    {
        targetKind = string.Empty;
        targetName = string.Empty;
        layer = string.Empty;

        var namespaceText = GetContainingNamespace(nullableType);
        layer = GetLayerLabel(namespaceText);

        if (nullableType.Parent is MethodDeclarationSyntax method &&
            ReferenceEquals(method.ReturnType, nullableType))
        {
            targetKind = "Return type";
            targetName = method.Identifier.ValueText;
            return true;
        }

        if (nullableType.Parent is PropertyDeclarationSyntax property &&
            ReferenceEquals(property.Type, nullableType))
        {
            targetKind = "Property";
            targetName = property.Identifier.ValueText;
            return true;
        }

        if (nullableType.Parent is VariableDeclarationSyntax variableDeclaration &&
            variableDeclaration.Parent is FieldDeclarationSyntax fieldDeclaration)
        {
            var variable = variableDeclaration.Variables.FirstOrDefault();
            if (variable is null)
            {
                return false;
            }

            targetKind = fieldDeclaration.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.ConstKeyword))
                ? "Constant"
                : "Field";
            targetName = variable.Identifier.ValueText;
            return true;
        }

        if (nullableType.Parent is ParameterSyntax parameter)
        {
            var owner = parameter.Parent?.Parent;
            if (owner is MethodDeclarationSyntax methodOwner)
            {
                targetKind = "Parameter";
                targetName = $"{methodOwner.Identifier.ValueText}.{parameter.Identifier.ValueText}";
                return true;
            }

            if (owner is ConstructorDeclarationSyntax constructorOwner)
            {
                targetKind = "Constructor parameter";
                targetName = $"{constructorOwner.Identifier.ValueText}.{parameter.Identifier.ValueText}";
                return true;
            }

            if (owner is RecordDeclarationSyntax recordOwner)
            {
                targetKind = "Record parameter";
                targetName = $"{recordOwner.Identifier.ValueText}.{parameter.Identifier.ValueText}";
                return true;
            }
        }

        return false;
    }
}
