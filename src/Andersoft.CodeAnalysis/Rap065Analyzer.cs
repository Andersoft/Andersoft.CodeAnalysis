using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap065Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP065",
        "MongoDB collection operations must be encapsulated by repositories",
        "MongoDB collection operation '{0}' must be encapsulated by Andersoft.Domain.IRepositoryBase<T, TId>; use the base repository and extend it only when its existing operations cannot express the required behavior",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeCollectionOperation, OperationKind.Invocation, OperationKind.MethodReference);
    }

    private static void AnalyzeCollectionOperation(OperationAnalysisContext context)
    {
        IMethodSymbol method;
        ITypeSymbol? receiverType;
        switch (context.Operation)
        {
            case IInvocationOperation invocation:
                method = invocation.TargetMethod;
                receiverType = invocation.Instance?.Type;
                break;
            case IMethodReferenceOperation reference:
                method = reference.Method;
                receiverType = reference.Instance?.Type;
                break;
            default:
                return;
        }

        var definition = method.ReducedFrom ?? method;
        var isCollectionOperation = IsMongoCollection(definition.ContainingType) ||
            (definition.ContainingType.SpecialType != SpecialType.System_Object && IsMongoCollection(receiverType)) ||
            (definition.IsExtensionMethod && definition.Parameters.Length > 0 && IsMongoCollection(definition.Parameters[0].Type));

        if (!isCollectionOperation || context.ContainingSymbol.ContainingType?.AllInterfaces.Any(IsRepositoryBase) == true)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, context.Operation.Syntax.GetLocation(), method.Name));
    }

    private static bool IsMongoCollection(ITypeSymbol? type, HashSet<ITypeSymbol>? visited = null)
    {
        switch (type)
        {
            case INamedTypeSymbol named:
                return IsMongoCollectionContract(named) || named.AllInterfaces.Any(IsMongoCollectionContract);
            case ITypeParameterSymbol parameter:
                visited ??= new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
                return visited.Add(parameter) && parameter.ConstraintTypes.Any(constraint => IsMongoCollection(constraint, visited));
            default:
                return false;
        }
    }

    private static bool IsMongoCollectionContract(INamedTypeSymbol type) =>
        type.TypeKind == TypeKind.Interface && type.MetadataName == "IMongoCollection`1" &&
        type.ContainingNamespace.ToDisplayString() == "MongoDB.Driver";

    private static bool IsRepositoryBase(INamedTypeSymbol type) =>
        type.TypeKind == TypeKind.Interface && type.MetadataName == "IRepositoryBase`2" &&
        type.ContainingNamespace.ToDisplayString() == "Andersoft.Domain";
}
