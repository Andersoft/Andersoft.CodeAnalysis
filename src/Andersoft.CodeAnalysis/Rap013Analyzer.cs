using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap013Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP013",
        "Nullable contract/state violation in domain/application",
        "Nullable contract/state violation in domain/application",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP013");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeRap013NullableContractsAndState(
        SyntaxNodeAnalysisContext context,
        NullableTypeSyntax nullableType,
        string targetKind,
        string targetName,
        string layer,
        string displayType)
    {
        Report(context, Diagnostic.Create(
            Rap013Analyzer.Rule,
            nullableType.GetLocation(),
            targetKind,
            targetName,
            layer,
            displayType));
    }
}
