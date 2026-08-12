using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Andersoft.CodeAnalysis;

internal static partial class ArchitectureConventionsAnalyzer
{
    private static bool TryGetPresentationMethod(SyntaxNodeAnalysisContext context, out MethodDeclarationSyntax method)
    {
        method = context.Node as MethodDeclarationSyntax ?? null!;
        return method is not null &&
            !IsGeneratedFile(method.SyntaxTree.FilePath) &&
            GetContainingNamespace(method).Contains(".Presentation", StringComparison.Ordinal);
    }

    private static bool IsEndpointHandler(MethodDeclarationSyntax method)
    {
        var containingClass = method.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (containingClass is null || !containingClass.Identifier.ValueText.EndsWith("Endpoint", StringComparison.Ordinal) ||
            method.Identifier.ValueText == "DefineEndpoint") return false;
        return containingClass.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(IsMapInvocation)
            .Any(invocation => invocation.ArgumentList.Arguments.Any(argument =>
                argument.Expression is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == method.Identifier.ValueText));
    }

    private static ITypeSymbol UnwrapTaskLike(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsGenericType: true } named &&
        named.ConstructedFrom.ToDisplayString() is "System.Threading.Tasks.Task<TResult>" or "System.Threading.Tasks.ValueTask<TResult>"
            ? named.TypeArguments[0]
            : type;

    private static bool ReturnsHttpResult(ITypeSymbol type)
    {
        var unwrapped = UnwrapTaskLike(type);
        return unwrapped.ToDisplayString() == "Microsoft.AspNetCore.Http.IResult" ||
            unwrapped.AllInterfaces.Any(iface => iface.ToDisplayString() == "Microsoft.AspNetCore.Http.IResult");
    }
}
