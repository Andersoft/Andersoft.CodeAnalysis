using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap049Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP049",
        "Endpoint class maps too many operations",
        "Endpoint class '{0}' maps {1} operations (max {2}) — split the resource capabilities into cohesive endpoint classes",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context) =>
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP049");
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private const int MaxEndpointOperationCount = 8;

    private static void AnalyzeEndpointOperationCount(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not ClassDeclarationSyntax classDeclaration ||
            IsGeneratedFile(classDeclaration.SyntaxTree.FilePath) ||
            !classDeclaration.Identifier.ValueText.EndsWith("Endpoint", StringComparison.Ordinal))
        {
            return;
        }

        var operationCount = classDeclaration.Members
            .OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.ValueText == "DefineEndpoint")
            .SelectMany(method => method.DescendantNodes().OfType<InvocationExpressionSyntax>())
            .Count(invocation => invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.ValueText is "MapGet" or "MapPost" or "MapPut" or "MapDelete" or "MapPatch" or "MapMethods");

        if (operationCount <= MaxEndpointOperationCount)
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap049Analyzer.Rule,
            classDeclaration.Identifier.GetLocation(),
            classDeclaration.Identifier.ValueText,
            operationCount,
            MaxEndpointOperationCount));
    }
}
