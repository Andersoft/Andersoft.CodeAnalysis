using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap054Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP054", "Endpoint receives competing current-user abstractions",
        "Method '{0}' receives both ClaimsPrincipal and '{1}' — resolve authentication into one request-scoped user abstraction",
        "Security", DiagnosticSeverity.Error, isEnabledByDefault: true);
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);
    public override void Initialize(AnalysisContext context) => ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP054");
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeDuplicateCurrentUserInputs(SyntaxNodeAnalysisContext context)
    {
        if (!TryGetPresentationMethod(context, out var method) ||
            context.SemanticModel.GetDeclaredSymbol(method) is not IMethodSymbol symbol) return;
        var principal = symbol.Parameters.FirstOrDefault(parameter => parameter.Type.ToDisplayString() == "System.Security.Claims.ClaimsPrincipal");
        var accessor = symbol.Parameters.FirstOrDefault(parameter => parameter.Type.Name.EndsWith("UserAccessor", System.StringComparison.Ordinal));
        if (principal is null || accessor is null) return;
        Report(context, Diagnostic.Create(Rap054Analyzer.Rule, method.Identifier.GetLocation(), symbol.Name, accessor.Type.Name));
    }
}
