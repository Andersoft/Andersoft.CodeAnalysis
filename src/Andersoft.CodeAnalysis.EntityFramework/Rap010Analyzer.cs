using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Andersoft.CodeAnalysis.EntityFramework;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap010Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP010",
        "Presentation contract exposes domain or EF type",
        "Presentation contract exposes domain or EF type",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        EfAnalyzer.InitializeRule(context, "RAP010");
    }
}

internal static partial class EfAnalyzer
{
    private static void AnalyzeRap010PresentationContractBoundary(
        SymbolAnalysisContext context,
        INamedTypeSymbol namedType,
        INamedTypeSymbol? dbContextSymbol)
    {
        if (namedType.DeclaredAccessibility != Accessibility.Public)
        {
            return;
        }

        var namespaceText = namedType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (!IsPresentationContractNamespace(namespaceText))
        {
            return;
        }

        foreach (var member in namedType.GetMembers())
        {
            if (member is IPropertySymbol property &&
                property.DeclaredAccessibility == Accessibility.Public &&
                IsForbiddenPresentationContractType(property.Type, dbContextSymbol))
            {
                Report(context, Diagnostic.Create(
                    Rap010Analyzer.Rule,
                    property.Locations[0],
                    property.Name,
                    property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            }

            if (member is IFieldSymbol field &&
                field.DeclaredAccessibility == Accessibility.Public &&
                IsForbiddenPresentationContractType(field.Type, dbContextSymbol))
            {
                Report(context, Diagnostic.Create(
                    Rap010Analyzer.Rule,
                    field.Locations[0],
                    field.Name,
                    field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            }
        }

        foreach (var constructor in namedType.InstanceConstructors)
        {
            if (constructor.IsImplicitlyDeclared)
            {
                continue;
            }

            foreach (var parameter in constructor.Parameters)
            {
                if (!IsForbiddenPresentationContractType(parameter.Type, dbContextSymbol))
                {
                    continue;
                }

                Report(context, Diagnostic.Create(
                    Rap010Analyzer.Rule,
                    parameter.Locations[0],
                    parameter.Name,
                    parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            }
        }
    }
}
