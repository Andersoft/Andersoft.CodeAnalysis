using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Andersoft.CodeAnalysis.EntityFramework;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap002Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP002",
        "Presentation depends on DbContext",
        "Presentation depends on DbContext",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        EfAnalyzer.InitializeRule(context, "RAP002");
    }
}

internal static partial class EfAnalyzer
{
    private static void AnalyzeRap002PresentationDbContextDependency(
        SymbolAnalysisContext context,
        INamedTypeSymbol namedType,
        INamedTypeSymbol? dbContextSymbol)
    {
        if (dbContextSymbol is null)
        {
            return;
        }

        foreach (var constructor in namedType.InstanceConstructors)
        {
            foreach (var parameter in constructor.Parameters)
            {
                if (parameter.Type is not INamedTypeSymbol parameterType)
                {
                    continue;
                }

                if (!InheritsOrImplements(parameterType, dbContextSymbol))
                {
                    continue;
                }

                Report(context, Diagnostic.Create(
                    Rap002Analyzer.Rule,
                    parameter.Locations[0],
                    namedType.Name,
                    parameterType.ToDisplayString()));
            }
        }
    }
}
