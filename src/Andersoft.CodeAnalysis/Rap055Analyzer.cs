using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap055Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP055", "Endpoint response contract is untyped",
        "Endpoint handler '{0}' returns untyped IResult — use a concrete HTTP result or Results<T1, ...> so response contracts remain compile-time visible",
        "Architecture", DiagnosticSeverity.Error, isEnabledByDefault: true);
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);
    public override void Initialize(AnalysisContext context) => ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP055");
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeUntypedEndpointResult(SyntaxNodeAnalysisContext context)
    {
        if (!TryGetPresentationMethod(context, out var method) || !IsEndpointHandler(method) ||
            context.SemanticModel.GetDeclaredSymbol(method) is not IMethodSymbol symbol) return;
        var resultType = UnwrapTaskLike(symbol.ReturnType);
        if (resultType.ToDisplayString() != "Microsoft.AspNetCore.Http.IResult") return;
        Report(context, Diagnostic.Create(Rap055Analyzer.Rule, method.ReturnType.GetLocation(), symbol.Name));
    }
}
