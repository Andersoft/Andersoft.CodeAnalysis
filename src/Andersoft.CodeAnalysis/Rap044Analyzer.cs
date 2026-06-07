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
public sealed class Rap044Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP044",
        "Middle man — class mostly forwards to another object",
        "Class '{0}': {1} of its {2} public methods only forward to '{3}' — a middle man; let callers use the target directly or collapse the wrapper",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP044");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    /// <summary>
    /// Below this many public methods a wrapper is too small to judge.
    /// </summary>
    private const int MinMiddleManPublicMethods = 4;

    private static void AnalyzeMiddleMan(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not ClassDeclarationSyntax classDeclaration)
        {
            return;
        }

        if (IsGeneratedFile(classDeclaration.SyntaxTree.FilePath) ||
            classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            return;
        }

        // Thin forwarding is the *required* shape in Presentation (RAP014).
        if (GetContainingNamespace(classDeclaration).Contains(".Presentation.", StringComparison.Ordinal))
        {
            return;
        }

        var publicMethods = classDeclaration.Members
            .OfType<MethodDeclarationSyntax>()
            .Where(method => method.Modifiers.Any(SyntaxKind.PublicKeyword) &&
                !method.Modifiers.Any(SyntaxKind.StaticKeyword) &&
                !method.Modifiers.Any(SyntaxKind.OverrideKeyword))
            .ToList();

        if (publicMethods.Count < MinMiddleManPublicMethods)
        {
            return;
        }

        if (context.SemanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol classSymbol)
        {
            return;
        }

        var delegationsByTarget = new Dictionary<ISymbol, int>(SymbolEqualityComparer.Default);

        foreach (var method in publicMethods)
        {
            var expression = method.ExpressionBody?.Expression ?? method.Body?.Statements.Count switch
            {
                1 => method.Body!.Statements[0] switch
                {
                    ReturnStatementSyntax returnStatement => returnStatement.Expression,
                    ExpressionStatementSyntax expressionStatement => expressionStatement.Expression,
                    _ => null,
                },
                _ => null,
            };

            while (expression is AwaitExpressionSyntax awaitExpression)
            {
                expression = awaitExpression.Expression;
            }

            if (expression is not InvocationExpressionSyntax invocation ||
                invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                memberAccess.Expression is not IdentifierNameSyntax receiver)
            {
                continue;
            }

            if (context.SemanticModel.GetSymbolInfo(receiver).Symbol is not ISymbol target ||
                target is not (IFieldSymbol or IPropertySymbol) ||
                !SymbolEqualityComparer.Default.Equals(target.ContainingType, classSymbol))
            {
                continue;
            }

            // A class implementing the same interface as the field it forwards
            // to is a decorator — an intentional middle man.
            var targetType = target is IFieldSymbol fieldTarget ? fieldTarget.Type : ((IPropertySymbol)target).Type;
            if (targetType.TypeKind == TypeKind.Interface &&
                classSymbol.AllInterfaces.Any(implemented => SymbolEqualityComparer.Default.Equals(implemented, targetType)))
            {
                return;
            }

            delegationsByTarget[target] = delegationsByTarget.TryGetValue(target, out var soFar) ? soFar + 1 : 1;
        }

        if (delegationsByTarget.Count == 0)
        {
            return;
        }

        var dominantTarget = delegationsByTarget.OrderByDescending(pair => pair.Value).First();
        if (dominantTarget.Value * 2 <= publicMethods.Count)
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap044Analyzer.Rule,
            classDeclaration.Identifier.GetLocation(),
            classDeclaration.Identifier.ValueText,
            dominantTarget.Value,
            publicMethods.Count,
            dominantTarget.Key.Name));
    }
}
