using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap045Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP045",
        "Lazy class — type with no responsibilities",
        "Class '{0}' has no members, no base types, and no attributes — a lazy class; remove it or give it a responsibility",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP045");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeLazyClass(SyntaxNodeAnalysisContext context)
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

        // Deliberately narrow: a base list marks a marker/discriminator type
        // and attributes mark framework hooks — both are intentional.
        if (classDeclaration.Members.Count > 0 ||
            classDeclaration.BaseList is not null ||
            classDeclaration.AttributeLists.Count > 0)
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap045Analyzer.Rule,
            classDeclaration.Identifier.GetLocation(),
            classDeclaration.Identifier.ValueText));
    }
}
