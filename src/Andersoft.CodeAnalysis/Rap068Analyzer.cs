using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap068Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP068",
        "Aggregate is too large",
        "Aggregate '{0}' has {1} members across {2} lines (max {3} members / {4} lines) and probably represents more than one aggregate; identify the logical domain boundaries and split it into the appropriate number of aggregate roots.",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP068");
    }
}
