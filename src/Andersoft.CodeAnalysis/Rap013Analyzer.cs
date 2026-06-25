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
        "Nullable '{2}' on {0} '{1}' is not allowed; {3}",
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

        Report(context, Diagnostic.Create(
            Rap013Analyzer.Rule,
            nullableType.GetLocation(),
            targetKind,
            targetName,
            displayType,
            recommendation));
    }

    // Tailors the recommended fix to the kind of member that exposed the nullable
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
