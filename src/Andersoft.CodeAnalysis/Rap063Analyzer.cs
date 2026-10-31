using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap063Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP063",
        "Use concrete models instead of JSON DOM contracts",
        "Contract '{0}' uses '{1}' — use a concrete class or record instead of a JSON DOM type",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context) =>
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP063");
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeJsonDomContract(SyntaxNodeAnalysisContext context)
    {
        if (IsGeneratedFile(context.Node.SyntaxTree.FilePath))
        {
            return;
        }

        (TypeSyntax? Type, string Name) contract = context.Node switch
        {
            ParameterSyntax parameter => (parameter.Type, parameter.Identifier.ValueText),
            MethodDeclarationSyntax method => (method.ReturnType, method.Identifier.ValueText),
            LocalFunctionStatementSyntax function => (function.ReturnType, function.Identifier.ValueText),
            DelegateDeclarationSyntax declaration => (declaration.ReturnType, declaration.Identifier.ValueText),
            PropertyDeclarationSyntax property => (property.Type, property.Identifier.ValueText),
            IndexerDeclarationSyntax indexer => (indexer.Type, "this[]"),
            FieldDeclarationSyntax field => (field.Declaration.Type, string.Join(", ", field.Declaration.Variables.Select(variable => variable.Identifier.ValueText))),
            EventFieldDeclarationSyntax field => (field.Declaration.Type, string.Join(", ", field.Declaration.Variables.Select(variable => variable.Identifier.ValueText))),
            EventDeclarationSyntax declaration => (declaration.Type, declaration.Identifier.ValueText),
            OperatorDeclarationSyntax declaration => (declaration.ReturnType, "operator " + declaration.OperatorToken.ValueText),
            ConversionOperatorDeclarationSyntax declaration => (declaration.Type, "operator " + declaration.Type),
            _ => (null, string.Empty)
        };

        if (contract.Type is null ||
            context.SemanticModel.GetTypeInfo(contract.Type, context.CancellationToken).Type is not { } type ||
            !ContainsJsonDomType(type))
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap063Analyzer.Rule,
            contract.Type.GetLocation(),
            contract.Name,
            type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }

    private static bool ContainsJsonDomType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            return ContainsJsonDomType(array.ElementType);
        }

        if (type is IPointerTypeSymbol pointer)
        {
            return ContainsJsonDomType(pointer.PointedAtType);
        }

        if (type is IFunctionPointerTypeSymbol function)
        {
            return ContainsJsonDomType(function.Signature.ReturnType) ||
                function.Signature.Parameters.Any(parameter => ContainsJsonDomType(parameter.Type));
        }

        if (type is not INamedTypeSymbol named || named.TypeKind == TypeKind.Error)
        {
            return false;
        }

        for (var current = named; current is not null; current = current.BaseType)
        {
            var ns = current.ContainingNamespace.ToDisplayString();
            if ((ns == "System.Text.Json" && current.MetadataName is "JsonElement" or "JsonDocument" or "JsonProperty") ||
                (ns == "System.Text.Json.Nodes" && current.MetadataName is "JsonNode" or "JsonObject" or "JsonArray" or "JsonValue") ||
                (ns == "Newtonsoft.Json.Linq" && current.MetadataName is "JToken" or "JObject" or "JArray" or "JValue" or "JContainer" or "JProperty" or "JConstructor" or "JRaw"))
            {
                return true;
            }
        }

        return named.TypeArguments.Any(ContainsJsonDomType) ||
            (named.ContainingType is not null && ContainsJsonDomType(named.ContainingType));
    }
}
