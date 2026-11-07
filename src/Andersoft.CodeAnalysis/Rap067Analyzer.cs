using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap067Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP067",
        "Inject strongly typed settings instead of configuration or options wrappers",
        "{0}; use builder.Services.AddOptions<TSettings>().BindConfiguration(\"SectionName\").ValidateDataAnnotations().ValidateOnStart(), then register the POCO as a singleton with builder.Services.AddSingleton(provider => provider.GetRequiredService<IOptions<TSettings>>().Value) and inject TSettings directly",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeDependency, SyntaxKind.Parameter, SyntaxKind.PropertyDeclaration);
        context.RegisterOperationAction(AnalyzeConfigurationRead,
            OperationKind.Invocation, OperationKind.MethodReference, OperationKind.PropertyReference);
    }

    private static void AnalyzeDependency(SyntaxNodeAnalysisContext context)
    {
        ITypeSymbol? type;
        SyntaxToken identifier;
        switch (context.Node)
        {
            case ParameterSyntax parameter:
                var symbol = context.SemanticModel.GetDeclaredSymbol(parameter, context.CancellationToken) as IParameterSymbol;
                if (symbol?.ContainingSymbol is not IMethodSymbol { MethodKind: MethodKind.Constructor } &&
                    !HasInjectionAttribute(symbol))
                {
                    return;
                }

                type = symbol?.Type;
                identifier = parameter.Identifier;
                break;
            case PropertyDeclarationSyntax property:
                var propertySymbol = context.SemanticModel.GetDeclaredSymbol(property, context.CancellationToken);
                if (!HasInjectionAttribute(propertySymbol))
                {
                    return;
                }

                type = propertySymbol?.Type;
                identifier = property.Identifier;
                break;
            default:
                return;
        }

        if (ContainsConfigurationDependency(type, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, identifier.GetLocation(),
                $"Dependency '{identifier.ValueText}' must not inject '{type!.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}'"));
        }
    }

    private static bool HasInjectionAttribute(ISymbol? symbol) =>
        symbol?.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() is
            "Microsoft.AspNetCore.Mvc.FromServicesAttribute" or
            "Microsoft.AspNetCore.Components.InjectAttribute" or
            "Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute") == true;

    private static bool ContainsConfigurationDependency(ITypeSymbol? type, HashSet<ITypeSymbol> visited)
    {
        if (type is null || !visited.Add(type))
        {
            return false;
        }

        return type switch
        {
            IArrayTypeSymbol array => ContainsConfigurationDependency(array.ElementType, visited),
            ITypeParameterSymbol parameter => parameter.ConstraintTypes.Any(constraint => ContainsConfigurationDependency(constraint, visited)),
            INamedTypeSymbol named => IsConfiguration(named) || IsOptionsContract(named) ||
                named.AllInterfaces.Any(IsOptionsContract) ||
                named.TypeArguments.Any(argument => ContainsConfigurationDependency(argument, visited)),
            _ => false
        };
    }

    private static bool IsOptionsContract(INamedTypeSymbol type) =>
        type.TypeKind == TypeKind.Interface && type.Arity == 1 &&
        type.Name is "IOptions" or "IOptionsSnapshot" or "IOptionsMonitor" &&
        type.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.Options";

    private static bool IsConfigurationContract(INamedTypeSymbol type) =>
        type.TypeKind == TypeKind.Interface && type.MetadataName == "IConfiguration" &&
        type.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.Configuration";

    private static bool IsConfiguration(ITypeSymbol? type) => type is INamedTypeSymbol named &&
        (IsConfigurationContract(named) || named.AllInterfaces.Any(IsConfigurationContract));

    private static void AnalyzeConfigurationRead(OperationAnalysisContext context)
    {
        if (context.Operation is IPropertyReferenceOperation property)
        {
            if ((property.Property.IsIndexer || property.Property.Name == "Value") &&
                (IsConfiguration(property.Property.ContainingType) || IsConfiguration(property.Instance?.Type)) &&
                !IsWriteOnly(property))
            {
                ReportRead(context, property.Property.IsIndexer ? "indexer" : "Value");
            }

            return;
        }

        var method = context.Operation switch
        {
            IInvocationOperation invocation => invocation.TargetMethod,
            IMethodReferenceOperation reference => reference.Method,
            _ => null
        };
        if (method is null)
        {
            return;
        }

        var definition = method.ReducedFrom ?? method;
        // Section navigation and Bind/Get<TPoco> are needed during startup binding.
        // Scalar Get<T> is a setting read, just like GetValue<T>.
        var isRead = definition.Name is "GetValue" or "GetConnectionString" or "GetChildren" or "AsEnumerable" ||
            (definition.Name == "Get" && !BindsSettingsObject(context.Operation, method));
        if (isRead && (IsConfiguration(definition.ContainingType) ||
            (definition.IsExtensionMethod && definition.Parameters.Length > 0 && IsConfiguration(definition.Parameters[0].Type))))
        {
            ReportRead(context, method.Name);
        }
    }

    private static bool BindsSettingsObject(IOperation operation, IMethodSymbol method)
    {
        if (method.TypeArguments.Length == 1)
        {
            return IsSettingsObject(method.TypeArguments[0]);
        }

        return operation is IInvocationOperation invocation && invocation.Arguments.Any(argument =>
            argument.Value is ITypeOfOperation typeOf && IsSettingsObject(typeOf.TypeOperand));
    }

    private static bool IsSettingsObject(ITypeSymbol type) =>
        type is INamedTypeSymbol { TypeKind: TypeKind.Class, SpecialType: SpecialType.None };

    private static bool IsWriteOnly(IPropertyReferenceOperation property) =>
        property.Parent is ISimpleAssignmentOperation assignment && assignment.Target == property;

    private static void ReportRead(OperationAnalysisContext context, string member) =>
        context.ReportDiagnostic(Diagnostic.Create(Rule, context.Operation.Syntax.GetLocation(),
            $"Configuration settings must not be read directly through '{member}'"));
}
