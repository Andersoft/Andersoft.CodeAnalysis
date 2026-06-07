using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap024Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP024",
        "Use entity-specific strongly typed IDs for ID-shaped members",
        "{0} '{1}' in {2} uses weak ID type '{3}'; use an entity-specific ID type (e.g. TaskId, AgentId, WorkflowId)",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP024");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeRap024StronglyTypedIdMembers(SyntaxNodeAnalysisContext context, string containingNamespace)
    {
        var layerName = IsDomainNamespace(containingNamespace) ? "Domain" : "Application";

        switch (context.Node)
        {
            case PropertyDeclarationSyntax property:
            {
                if (property.Identifier.ValueText == "Id")
                {
                    return;
                }

                if (!IsIdShapedMemberName(property.Identifier.ValueText))
                {
                    return;
                }

                if (context.SemanticModel.GetTypeInfo(property.Type).Type is not ITypeSymbol propertyType || !IsPrimitiveIdType(propertyType))
                {
                    return;
                }

                Report(context, Diagnostic.Create(
                    Rap024Analyzer.Rule,
                    property.Type.GetLocation(),
                    "Property",
                    property.Identifier.ValueText,
                    layerName,
                    propertyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                return;
            }
            case FieldDeclarationSyntax field:
            {
                if (context.SemanticModel.GetTypeInfo(field.Declaration.Type).Type is not ITypeSymbol fieldType || !IsPrimitiveIdType(fieldType))
                {
                    return;
                }

                foreach (var variable in field.Declaration.Variables)
                {
                    if (!IsIdShapedMemberName(variable.Identifier.ValueText))
                    {
                        continue;
                    }

                    Report(context, Diagnostic.Create(
                        Rap024Analyzer.Rule,
                        field.Declaration.Type.GetLocation(),
                        "Field",
                        variable.Identifier.ValueText,
                        layerName,
                        fieldType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                }

                return;
            }
        }
    }
}
