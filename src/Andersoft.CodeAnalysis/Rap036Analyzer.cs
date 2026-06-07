using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap036Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP036",
        "Long parameter list",
        "{0} has {1} parameters (excluding CancellationToken), which exceeds the maximum of {2} — group related parameters into a parameter object (record)",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP036");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private const int MaxParameterListLength = 5;

    private static void AnalyzeLongParameterList(SyntaxNodeAnalysisContext context)
    {
        if (IsGeneratedFile(context.Node.SyntaxTree.FilePath))
        {
            return;
        }

        // Overrides and explicit interface implementations cannot change their
        // signature — the declaration that owns the contract gets flagged.
        var (parameterList, identifier, displayName) = context.Node switch
        {
            MethodDeclarationSyntax method when !method.Modifiers.Any(SyntaxKind.OverrideKeyword) && method.ExplicitInterfaceSpecifier is null
                => (method.ParameterList, method.Identifier, $"Method '{method.Identifier.ValueText}'"),
            ConstructorDeclarationSyntax ctor => (ctor.ParameterList, ctor.Identifier, $"Constructor '{ctor.Identifier.ValueText}'"),
            LocalFunctionStatementSyntax localFunction => (localFunction.ParameterList, localFunction.Identifier, $"Local function '{localFunction.Identifier.ValueText}'"),
            DelegateDeclarationSyntax @delegate => (@delegate.ParameterList, @delegate.Identifier, $"Delegate '{@delegate.Identifier.ValueText}'"),
            _ => (null, default, string.Empty),
        };

        if (parameterList is null)
        {
            return;
        }

        var count = parameterList.Parameters.Count(parameter => !IsCancellationTokenParameter(parameter));
        if (count <= MaxParameterListLength)
        {
            return;
        }

        // Implicit interface implementations restate the interface's signature;
        // the interface declaration is the one that gets flagged.
        if (context.Node is MethodDeclarationSyntax candidate &&
            context.SemanticModel.GetDeclaredSymbol(candidate) is IMethodSymbol methodSymbol &&
            ImplementsInterfaceMember(methodSymbol))
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap036Analyzer.Rule,
            identifier.GetLocation(),
            displayName,
            count,
            MaxParameterListLength));
    }

    private static bool ImplementsInterfaceMember(IMethodSymbol method)
    {
        foreach (var implementedInterface in method.ContainingType.AllInterfaces)
        {
            foreach (var member in implementedInterface.GetMembers(method.Name).OfType<IMethodSymbol>())
            {
                if (SymbolEqualityComparer.Default.Equals(
                        method.ContainingType.FindImplementationForInterfaceMember(member), method))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsCancellationTokenParameter(ParameterSyntax parameter)
    {
        var text = parameter.Type?.ToString();
        return text is not null &&
            (text == "CancellationToken" || text.EndsWith(".CancellationToken", StringComparison.Ordinal));
    }
}
