using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap046Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP046",
        "Anemic domain class — data without behavior",
        "Domain class '{0}' exposes {1} publicly settable properties and no methods — an anemic domain model; move the behavior that manipulates this state into the entity and encapsulate the setters",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP046");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    /// <summary>
    /// Publicly settable properties a behavior-less domain class may expose
    /// before it counts as anemic.
    /// </summary>
    private const int MinAnemicSettableProperties = 3;

    private static void AnalyzeAnemicDomainClass(SyntaxNodeAnalysisContext context)
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

        // The data-class smell is idiomatic for DTOs/contracts — only the
        // domain layer must carry behavior.
        if (!GetContainingNamespace(classDeclaration).Contains(".Domain.", StringComparison.Ordinal))
        {
            return;
        }

        if (classDeclaration.Members.OfType<MethodDeclarationSyntax>().Any())
        {
            return;
        }

        var settableProperties = classDeclaration.Members
            .OfType<PropertyDeclarationSyntax>()
            .Count(property => property.Modifiers.Any(SyntaxKind.PublicKeyword) &&
                property.AccessorList?.Accessors.Any(accessor =>
                    accessor.IsKind(SyntaxKind.SetAccessorDeclaration) && accessor.Modifiers.Count == 0) == true);

        if (settableProperties < MinAnemicSettableProperties)
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap046Analyzer.Rule,
            classDeclaration.Identifier.GetLocation(),
            classDeclaration.Identifier.ValueText,
            settableProperties));
    }
}
