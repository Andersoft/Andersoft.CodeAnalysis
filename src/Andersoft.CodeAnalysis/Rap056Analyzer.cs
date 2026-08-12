using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap056Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP056", "Boolean parameter controls HTTP response semantics",
        "Method '{0}' uses Boolean parameter '{1}' to select HTTP results — split the mappings into intention-revealing methods",
        "Architecture", DiagnosticSeverity.Error, isEnabledByDefault: true);
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);
    public override void Initialize(AnalysisContext context) => ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP056");
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeBooleanHttpMappingParameter(SyntaxNodeAnalysisContext context)
    {
        if (!TryGetPresentationMethod(context, out var method) ||
            context.SemanticModel.GetDeclaredSymbol(method) is not IMethodSymbol symbol ||
            !ReturnsHttpResult(symbol.ReturnType)) return;
        foreach (var parameter in symbol.Parameters.Where(parameter => parameter.Type.SpecialType == SpecialType.System_Boolean))
        {
            var parameterReferences = method.DescendantNodes().OfType<IdentifierNameSyntax>()
                .Where(identifier => SymbolEqualityComparer.Default.Equals(context.SemanticModel.GetSymbolInfo(identifier).Symbol, parameter));
            if (!parameterReferences.Any(reference => reference.FirstAncestorOrSelf<ConditionalExpressionSyntax>() is not null ||
                reference.FirstAncestorOrSelf<WhenClauseSyntax>() is not null ||
                reference.FirstAncestorOrSelf<IfStatementSyntax>() is not null)) continue;
            Report(context, Diagnostic.Create(Rap056Analyzer.Rule, parameter.Locations[0], symbol.Name, parameter.Name));
        }
    }
}
