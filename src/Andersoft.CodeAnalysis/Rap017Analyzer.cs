using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap017Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP017",
        "Disallowed global now usage",
        "Disallowed global now usage",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP017");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeRap017GlobalNowUsage(
        SyntaxNodeAnalysisContext context,
        MemberAccessExpressionSyntax memberAccess,
        string containingNamespace,
        string containingType,
        string propertyName)
    {
        if (IsAllowedTimeBoundaryUsage(memberAccess.SyntaxTree.FilePath, containingNamespace))
        {
            return;
        }

        var owner = context.ContainingSymbol?.ContainingType?.Name ?? "UnknownType";
        Report(context, Diagnostic.Create(
            Rap017Analyzer.Rule,
            memberAccess.GetLocation(),
            $"{containingType}.{propertyName}",
            owner));
    }
}
