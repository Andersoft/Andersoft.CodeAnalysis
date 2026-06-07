using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap012Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP012",
        "Presentation dependency violation",
        "Presentation dependency violation",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP012");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeRap012PresentationDependency(SymbolAnalysisContext context, INamedTypeSymbol namedType)
    {
        foreach (var constructor in namedType.InstanceConstructors)
        {
            foreach (var parameter in constructor.Parameters)
            {
                if (parameter.Type is not INamedTypeSymbol parameterType)
                {
                    continue;
                }

                if (!IsAllowedPresentationDependency(parameterType) && IsDisallowedPresentationDependency(parameterType))
                {
                    Report(context, Diagnostic.Create(
                        Rap012Analyzer.Rule,
                        parameter.Locations[0],
                        namedType.Name,
                        parameterType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                }
            }
        }
    }
}
