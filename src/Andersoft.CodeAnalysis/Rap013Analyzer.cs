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
        "Nullable contract/state violation",
        "{0} on {1} '{2}' is not allowed; {3}",
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
        string displayType)
    {
        var innerType = nullableType.ElementType.ToString();
        var recommendation = GetRap013Recommendation(targetKind, innerType, displayType);

        ReportRap013(
            context,
            nullableType.GetLocation(),
            $"Nullable '{displayType}'",
            targetKind,
            targetName,
            recommendation);
    }

    // Flags a function whose result is modelled with the Option pattern (directly,
    // or wrapped in Task<>/ValueTask<>). Option<T> is the right shape for optional
    // *state*, but a function result should express success/failure with OneOf.
    private static void AnalyzeRap013OptionalReturnType(
        SyntaxNodeAnalysisContext context,
        TypeSyntax returnType,
        string ownerName)
    {
        if (!TryGetOptionTypeArgument(UnwrapTaskLikeReturnType(returnType), out var innerType))
        {
            return;
        }

        var displayType = returnType.ToString();
        var recommendation = GetRap013Recommendation("Return type", innerType, displayType);

        ReportRap013(
            context,
            returnType.GetLocation(),
            $"Optional return type '{displayType}'",
            "Return type",
            ownerName,
            recommendation);
    }

    private static void ReportRap013(
        SyntaxNodeAnalysisContext context,
        Location location,
        string subject,
        string targetKind,
        string targetName,
        string recommendation)
    {
        Report(context, Diagnostic.Create(
            Rap013Analyzer.Rule,
            location,
            subject,
            targetKind,
            targetName,
            recommendation));
    }

    // Tailors the recommended fix to the kind of member that exposed the optional
    // type. Return types model success/failure with the OneOf pattern; everything
    // else (properties, fields, parameters) models optional state with Option<T>.
    private static string GetRap013Recommendation(string targetKind, string innerType, string displayType)
    {
        if (string.Equals(targetKind, "Return type", System.StringComparison.Ordinal))
        {
            return $"return OneOf<{innerType}, TError> (e.g. 'OneOf<Foo, FooError> SomeFunction()') instead of returning {displayType}";
        }

        return $"use the Option pattern 'Option<{innerType}>' instead of '{displayType}'";
    }
}
