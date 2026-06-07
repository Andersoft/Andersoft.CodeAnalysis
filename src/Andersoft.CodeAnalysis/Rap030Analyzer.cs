using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap030Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP030",
        "CancellationToken parameter should not have a default value",
        "CancellationToken parameter '{0}' has a default value — remove the default so callers must explicitly pass a token or CancellationToken.None",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP030");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private const string CancellationTokenFullName = "System.Threading.CancellationToken";

    private static void AnalyzeDefaultCancellationToken(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not ParameterSyntax parameter)
        {
            return;
        }

        // Fast text-level check: parameter must have a default value
        if (parameter.Default is null)
        {
            return;
        }

        if (IsGeneratedFile(parameter.SyntaxTree.FilePath))
        {
            return;
        }

        // Verify the parameter type is actually CancellationToken via SemanticModel
        var typeInfo = context.SemanticModel.GetTypeInfo(parameter.Type!);
        if (typeInfo.Type?.ToDisplayString() != CancellationTokenFullName)
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap030Analyzer.Rule,
            parameter.Default.GetLocation(),
            parameter.Identifier.ValueText));
    }
}
