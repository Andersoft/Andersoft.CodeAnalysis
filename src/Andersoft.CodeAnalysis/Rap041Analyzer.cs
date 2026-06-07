using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap041Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP041",
        "Temporary field — instance state used by a single method",
        "Field '{0}' is only used inside '{1}' and is assigned there before being read — a temporary field; make it a local variable (or pass it as a parameter)",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP041");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeTemporaryField(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not ClassDeclarationSyntax classDeclaration)
        {
            return;
        }

        if (IsGeneratedFile(classDeclaration.SyntaxTree.FilePath))
        {
            return;
        }

        // Other parts of a partial class may use the field — skip to stay sound.
        if (classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            return;
        }

        foreach (var field in classDeclaration.Members.OfType<FieldDeclarationSyntax>())
        {
            // Only private, mutable, instance fields without an initializer can
            // be demoted to locals.
            if (field.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.StaticKeyword) ||
                    modifier.IsKind(SyntaxKind.ConstKeyword) ||
                    modifier.IsKind(SyntaxKind.ReadOnlyKeyword) ||
                    modifier.IsKind(SyntaxKind.PublicKeyword) ||
                    modifier.IsKind(SyntaxKind.InternalKeyword) ||
                    modifier.IsKind(SyntaxKind.ProtectedKeyword)))
            {
                continue;
            }

            foreach (var declarator in field.Declaration.Variables)
            {
                if (declarator.Initializer is not null)
                {
                    continue;
                }

                AnalyzeTemporaryFieldCandidate(context, classDeclaration, declarator);
            }
        }
    }

    private static void AnalyzeTemporaryFieldCandidate(
        SyntaxNodeAnalysisContext context,
        ClassDeclarationSyntax classDeclaration,
        VariableDeclaratorSyntax declarator)
    {
        if (context.SemanticModel.GetDeclaredSymbol(declarator) is not IFieldSymbol fieldSymbol)
        {
            return;
        }

        MethodDeclarationSyntax? containingMethod = null;
        var references = new List<IdentifierNameSyntax>();

        foreach (var identifier in classDeclaration.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Identifier.ValueText != declarator.Identifier.ValueText)
            {
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(context.SemanticModel.GetSymbolInfo(identifier).Symbol, fieldSymbol))
            {
                continue;
            }

            // A reference outside an ordinary method (constructor, property, …)
            // means the field carries state between members — not temporary.
            var method = identifier.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (method is null || (containingMethod is not null && method != containingMethod))
            {
                return;
            }

            containingMethod = method;
            references.Add(identifier);
        }

        if (containingMethod is null || references.Count < 2)
        {
            return;
        }

        // Only flag the set-then-use pattern: the first reference in execution
        // order must be a plain write. Fields read first (caches, counters)
        // legitimately persist across calls.
        var first = references.OrderBy(reference => reference.SpanStart).First();
        var target = first.Parent is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } thisAccess && thisAccess.Name == first
            ? (ExpressionSyntax)thisAccess
            : first;

        if (target.Parent is not AssignmentExpressionSyntax assignment ||
            assignment.Left != target ||
            !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap041Analyzer.Rule,
            declarator.Identifier.GetLocation(),
            declarator.Identifier.ValueText,
            containingMethod.Identifier.ValueText));
    }
}
