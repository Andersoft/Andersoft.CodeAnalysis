using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap064Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP064",
        "Message handlers must not be injected as dependencies",
        "'{0}' depends on message handler type '{1}' — handlers are processing entry points, not injectable services; dispatch a message instead",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context) =>
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP064");
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeMessageHandlerDependency(SyntaxNodeAnalysisContext context)
    {
        if (IsGeneratedFile(context.Node.SyntaxTree.FilePath))
        {
            return;
        }

        switch (context.Node)
        {
            case ParameterSyntax parameter:
                var symbol = context.SemanticModel.GetDeclaredSymbol(parameter, context.CancellationToken) as IParameterSymbol;
                ReportMessageHandlerDependency(context, symbol?.Type, parameter.Identifier);
                break;
            case PropertyDeclarationSyntax property:
                ReportMessageHandlerDependency(context,
                    context.SemanticModel.GetTypeInfo(property.Type, context.CancellationToken).Type, property.Identifier);
                break;
            case FieldDeclarationSyntax field:
                var type = context.SemanticModel.GetTypeInfo(field.Declaration.Type, context.CancellationToken).Type;
                foreach (var variable in field.Declaration.Variables)
                {
                    ReportMessageHandlerDependency(context, type, variable.Identifier);
                }
                break;
        }
    }

    private static void ReportMessageHandlerDependency(SyntaxNodeAnalysisContext context, ITypeSymbol? type, SyntaxToken identifier)
    {
        if (type is null || !ContainsMessageHandler(type, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)))
        {
            return;
        }

        Report(context, Diagnostic.Create(Rap064Analyzer.Rule, identifier.GetLocation(), identifier.ValueText,
            type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }

    private static bool ContainsMessageHandler(ITypeSymbol type, HashSet<ITypeSymbol> visited)
    {
        if (!visited.Add(type))
        {
            return false;
        }

        return type switch
        {
            IArrayTypeSymbol array => ContainsMessageHandler(array.ElementType, visited),
            ITypeParameterSymbol parameter => parameter.ConstraintTypes.Any(constraint => ContainsMessageHandler(constraint, visited)),
            INamedTypeSymbol named => IsMessageHandlerContract(named) || named.AllInterfaces.Any(IsMessageHandlerContract) ||
                named.TypeArguments.Any(argument => ContainsMessageHandler(argument, visited)),
            _ => false
        };
    }

    private static bool IsMessageHandlerContract(INamedTypeSymbol type) =>
        type.TypeKind == TypeKind.Interface && type.Name == "IMessageHandler" && type.Arity is 1 or 2 &&
        type.ContainingNamespace.ToDisplayString() == "Andersoft.Messaging.Abstractions";
}
