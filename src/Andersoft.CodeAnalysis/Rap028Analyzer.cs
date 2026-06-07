using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap028Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP028",
        "Missing CancellationToken on async call",
        "Call to '{0}' accepts a CancellationToken but none was provided — pass one to allow cooperative cancellation",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP028");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    /// <summary>
    /// Full type name used to identify CancellationToken parameters.
    /// </summary>
    private const string CancellationTokenTypeName = "System.Threading.CancellationToken";

    private static void AnalyzeMissingCancellationToken(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
        {
            return;
        }

        if (IsGeneratedFile(invocation.SyntaxTree.FilePath))
        {
            return;
        }

        if (context.SemanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol)
        {
            return;
        }

        // Only check methods that accept a CancellationToken
        var ctParameter = FindCancellationTokenParameter(methodSymbol);
        if (ctParameter is null)
        {
            return;
        }

        // Check if the invocation already provides a CancellationToken argument
        if (InvocationProvidesCancellationToken(invocation, ctParameter))
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap028Analyzer.Rule,
            invocation.GetLocation(),
            methodSymbol.Name));
    }

    /// <summary>
    /// Finds the CancellationToken parameter on <paramref name="methodSymbol"/>, if any.
    /// Returns null when the method does not accept a CancellationToken.
    /// </summary>
    private static IParameterSymbol? FindCancellationTokenParameter(IMethodSymbol methodSymbol)
    {
        foreach (var parameter in methodSymbol.Parameters)
        {
            if (parameter.Type.ToDisplayString() == CancellationTokenTypeName)
            {
                return parameter;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns true when <paramref name="invocation"/> provides the CancellationToken
    /// argument corresponding to <paramref name="ctParameter"/>, either by position or
    /// by named argument.
    /// </summary>
    private static bool InvocationProvidesCancellationToken(
        InvocationExpressionSyntax invocation,
        IParameterSymbol ctParameter)
    {
        var args = invocation.ArgumentList.Arguments;

        // Named argument: argument.NameColon matches the parameter name
        foreach (var argument in args)
        {
            if (argument.NameColon?.Name.Identifier.ValueText == ctParameter.Name)
            {
                return true;
            }
        }

        // Positional argument: the parameter's ordinal is within range
        if (ctParameter.Ordinal < args.Count)
        {
            // If there's a positional arg at this index, make sure it's not a named
            // argument targeting a different parameter (unusual but possible).
            var candidate = args[ctParameter.Ordinal];
            if (candidate.NameColon is null)
            {
                return true;
            }
        }

        return false;
    }
}
