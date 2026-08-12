using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap053Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP053",
        "Created response uses an unregistered route",
        "Created location '{0}' does not match a route mapped by this endpoint class — use CreatedAtRoute or a registered resource route",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);
    public override void Initialize(AnalysisContext context) => ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP053");
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeCreatedLocation(SyntaxNodeAnalysisContext context)
    {
        if (!TryGetPresentationMethod(context, out var method)) return;
        var containingClass = method.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (containingClass is null) return;

        var mappedRoutes = containingClass.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(IsMapInvocation)
            .Select(invocation => invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression)
            .OfType<LiteralExpressionSyntax>()
            .Select(literal => literal.Token.ValueText.Trim('/'))
            .ToArray();

        foreach (var invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (context.SemanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol symbol ||
                symbol.Name != "Created" ||
                symbol.ContainingType.ToDisplayString() is not ("Microsoft.AspNetCore.Http.Results" or "Microsoft.AspNetCore.Http.TypedResults") ||
                invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is not ExpressionSyntax locationExpression ||
                !TryGetRouteShape(locationExpression, context.SemanticModel, out var location))
            {
                continue;
            }

            var normalizedLocation = location.Trim('/');
            if (mappedRoutes.Any(route => RouteShapesMatch(route, normalizedLocation))) continue;
            Report(context, Diagnostic.Create(Rap053Analyzer.Rule, locationExpression.GetLocation(), location));
        }
    }

    private static bool IsMapInvocation(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax member &&
        member.Name.Identifier.ValueText is "MapGet" or "MapPost" or "MapPut" or "MapDelete" or "MapPatch";

    private static bool TryGetRouteShape(ExpressionSyntax expression, SemanticModel semanticModel, out string route)
    {
        var constant = semanticModel.GetConstantValue(expression);
        if (constant.HasValue && constant.Value is string value)
        {
            route = value;
            return true;
        }

        if (expression is InterpolatedStringExpressionSyntax interpolated)
        {
            route = string.Concat(interpolated.Contents.Select(content => content is InterpolatedStringTextSyntax text
                ? text.TextToken.ValueText
                : "{}"));
            return true;
        }

        route = string.Empty;
        return false;
    }

    private static bool RouteShapesMatch(string mapped, string location)
    {
        var mappedSegments = mapped.Split('/');
        var locationSegments = location.Split('/');
        if (mappedSegments.Length != locationSegments.Length) return false;
        for (var i = 0; i < mappedSegments.Length; i++)
        {
            if (mappedSegments[i].StartsWith("{", StringComparison.Ordinal) || locationSegments[i] == "{}") continue;
            if (!string.Equals(mappedSegments[i], locationSegments[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }
}
