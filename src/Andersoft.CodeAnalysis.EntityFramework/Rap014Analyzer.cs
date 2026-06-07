using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Andersoft.CodeAnalysis.EntityFramework;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap014Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP014",
        "Presentation contains business logic",
        "Presentation contains business logic",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        EfAnalyzer.InitializeRule(context, "RAP014");
    }
}

internal static partial class EfAnalyzer
{
    private static void AnalyzeRap014PresentationBusinessLogic(
        SyntaxNodeAnalysisContext context,
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol)
    {
        foreach (var invocation in methodDeclaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (context.SemanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol called)
            {
                continue;
            }

            var owner = called.ContainingType;
            if (owner is null)
            {
                continue;
            }

            var ownerNamespace = owner.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            if (ownerNamespace.Contains(".Domain.", StringComparison.Ordinal) ||
                ownerNamespace.Contains(".Infrastructure.", StringComparison.Ordinal) ||
                owner.Name.Contains("Repository", StringComparison.Ordinal) ||
                owner.Name.Contains("DbContext", StringComparison.Ordinal))
            {
                Report(context, Diagnostic.Create(
                    Rap014Analyzer.Rule,
                    invocation.GetLocation(),
                    methodSymbol.Name,
                    owner.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            }
        }

        foreach (var assignment in methodDeclaration.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            var assignedSymbol = context.SemanticModel.GetSymbolInfo(assignment.Left).Symbol;
            if (assignedSymbol is IPropertySymbol property && IsDomainOrInfrastructureType(property.ContainingType))
            {
                Report(context, Diagnostic.Create(
                    Rap014Analyzer.Rule,
                    assignment.GetLocation(),
                    methodSymbol.Name,
                    property.ContainingType!.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            }
        }
    }
}
