using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap042Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP042",
        "Feature envy — method is more interested in another class",
        "Method '{0}' accesses {1} data members of '{2}' but only {3} of its own type — feature envy; move the logic next to the data it manipulates",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP042");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    /// <summary>
    /// Minimum data accesses on a single foreign type before envy is considered.
    /// </summary>
    private const int MinFeatureEnvyForeignAccesses = 6;

    private static void AnalyzeFeatureEnvy(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not MethodDeclarationSyntax methodDeclaration)
        {
            return;
        }

        if (IsGeneratedFile(methodDeclaration.SyntaxTree.FilePath))
        {
            return;
        }

        var body = (SyntaxNode?)methodDeclaration.Body ?? methodDeclaration.ExpressionBody;
        if (body is null)
        {
            return;
        }

        // Mapping members legitimately read many members of one source object.
        var methodName = methodDeclaration.Identifier.ValueText;
        if (methodName.StartsWith("Map", StringComparison.Ordinal) ||
            methodName.StartsWith("To", StringComparison.Ordinal) ||
            methodName.StartsWith("From", StringComparison.Ordinal) ||
            methodName.StartsWith("Convert", StringComparison.Ordinal))
        {
            return;
        }

        if (context.SemanticModel.GetDeclaredSymbol(methodDeclaration) is not IMethodSymbol methodSymbol ||
            methodSymbol.IsStatic)
        {
            return;
        }

        var containingType = methodSymbol.ContainingType;
        var ownAccesses = 0;
        var foreignAccesses = new Dictionary<INamedTypeSymbol, int>(SymbolEqualityComparer.Default);

        // Lambdas are declarative contexts (LINQ predicates, EF configuration)
        // — member accesses inside them are not logic that could move.
        foreach (var node in body.DescendantNodes(n => n is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax))
        {
            if (node is MemberAccessExpressionSyntax memberAccess && memberAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression))
            {
                if (context.SemanticModel.GetSymbolInfo(memberAccess).Symbol is not ISymbol accessed ||
                    accessed is not (IPropertySymbol or IFieldSymbol) ||
                    accessed.IsStatic)
                {
                    continue;
                }

                if (memberAccess.Expression is ThisExpressionSyntax)
                {
                    ownAccesses++;
                    continue;
                }

                if (context.SemanticModel.GetTypeInfo(memberAccess.Expression).Type is not INamedTypeSymbol receiverType)
                {
                    continue;
                }

                if (IsSelfOrBaseType(receiverType, containingType))
                {
                    ownAccesses++;
                }
                else if (receiverType.TypeKind is TypeKind.Class or TypeKind.Struct &&
                    SymbolEqualityComparer.Default.Equals(receiverType.ContainingAssembly, context.SemanticModel.Compilation.Assembly) &&
                    IsDomainLayerType(receiverType))
                {
                    // Only domain types can meaningfully host the envious logic —
                    // commands/DTOs/config objects are data carriers by design.
                    foreignAccesses[receiverType] = foreignAccesses.TryGetValue(receiverType, out var soFar) ? soFar + 1 : 1;
                }
            }
            else if (node is IdentifierNameSyntax identifier &&
                identifier.Parent is not MemberAccessExpressionSyntax &&
                context.SemanticModel.GetSymbolInfo(identifier).Symbol is (IPropertySymbol or IFieldSymbol) and { IsStatic: false } ownCandidate &&
                SymbolEqualityComparer.Default.Equals(ownCandidate.ContainingType, containingType))
            {
                // Implicit-this access to the method's own state.
                ownAccesses++;
            }
        }

        if (foreignAccesses.Count == 0)
        {
            return;
        }

        var enviedType = foreignAccesses.OrderByDescending(pair => pair.Value).First();
        if (enviedType.Value < MinFeatureEnvyForeignAccesses || enviedType.Value <= 2 * ownAccesses)
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap042Analyzer.Rule,
            methodDeclaration.Identifier.GetLocation(),
            methodName,
            enviedType.Value,
            enviedType.Key.Name,
            ownAccesses));
    }

    private static bool IsDomainLayerType(INamedTypeSymbol type)
    {
        var namespaceText = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return (namespaceText + ".").Contains(".Domain.");
    }

    private static bool IsSelfOrBaseType(INamedTypeSymbol candidate, INamedTypeSymbol containingType)
    {
        for (var current = (INamedTypeSymbol?)containingType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, candidate))
            {
                return true;
            }
        }

        return false;
    }
}
