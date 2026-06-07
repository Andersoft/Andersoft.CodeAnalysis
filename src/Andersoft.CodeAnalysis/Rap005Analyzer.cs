using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap005Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP005",
        "Primitive ID parameter in domain/application API",
        "Parameter '{0}' uses weak ID type '{1}'; use an entity-specific strongly typed ID",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP005");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeRap005PrimitiveIdParameter(
        SyntaxNodeAnalysisContext context,
        IParameterSymbol parameter,
        string typeName)
    {
        Report(context, Diagnostic.Create(
            Rap005Analyzer.Rule,
            parameter.Locations[0],
            parameter.Name,
            typeName));
    }
}
