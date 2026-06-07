using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using Microsoft.CodeAnalysis.Text;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap006Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP006",
        "Banned generic file name",
        "Banned generic file name",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP006");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeRap006BannedFileName(SyntaxTreeAnalysisContext context, string fileName)
    {
        if (fileName.EndsWith("Dtos.cs", StringComparison.Ordinal) ||
            fileName.EndsWith("Handlers.cs", StringComparison.Ordinal) ||
            fileName is "Helpers.cs" or "Utils.cs" or "Manager.cs" or "Processor.cs")
        {
            var location = Location.Create(context.Tree, new TextSpan(0, 0));
            Report(context, Diagnostic.Create(Rap006Analyzer.Rule, location, fileName));
        }
    }
}
