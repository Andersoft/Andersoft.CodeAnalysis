using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap061Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP061",
        "Message handler result must model success and error",
        "Message handler '{0}' returns '{1}' — use OneOf<TSuccess, TError>",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context) =>
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP061");
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeMessageHandlerReturnType(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol namedType ||
            namedType.TypeKind is not (TypeKind.Class or TypeKind.Struct) ||
            namedType.Locations.IsDefaultOrEmpty ||
            namedType.Locations[0].SourceTree is null ||
            IsGeneratedFile(namedType.Locations[0].SourceTree!.FilePath))
        {
            return;
        }

        foreach (var handlerInterface in namedType.AllInterfaces)
        {
            if (handlerInterface.Arity != 2 ||
                handlerInterface.Name != "IMessageHandler" ||
                handlerInterface.ContainingNamespace?.ToDisplayString() != "Andersoft.Messaging.Abstractions")
            {
                continue;
            }

            var resultType = handlerInterface.TypeArguments[1];
            if (resultType is INamedTypeSymbol
                {
                    Arity: 2,
                    Name: "OneOf",
                    ContainingNamespace: { } oneOfNamespace
                } && oneOfNamespace.ToDisplayString() == "OneOf")
            {
                continue;
            }

            Report(context, Diagnostic.Create(
                Rap061Analyzer.Rule,
                namedType.Locations[0],
                namedType.Name,
                resultType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }
}
