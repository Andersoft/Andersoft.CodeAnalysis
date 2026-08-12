using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap052Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP052",
        "Server-sent events are written manually",
        "Method '{0}' manually writes the server-sent events protocol — return TypedResults.ServerSentEvents from an async event sequence instead",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context) =>
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP052");
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeManualServerSentEvents(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not MethodDeclarationSyntax method ||
            IsGeneratedFile(method.SyntaxTree.FilePath) ||
            !GetContainingNamespace(method).Contains(".Presentation", StringComparison.Ordinal))
        {
            return;
        }

        var body = (SyntaxNode?)method.Body ?? method.ExpressionBody;
        if (body is null || !ContainsEventStreamContentType(body, context.SemanticModel))
        {
            return;
        }

        var manualWrite = body.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(invocation => IsManualResponseWrite(invocation, context.SemanticModel));
        if (manualWrite is null)
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap052Analyzer.Rule,
            manualWrite.GetLocation(),
            method.Identifier.ValueText));
    }

    private static bool ContainsEventStreamContentType(SyntaxNode body, SemanticModel semanticModel)
    {
        foreach (var expression in body.DescendantNodes().OfType<ExpressionSyntax>())
        {
            var constant = semanticModel.GetConstantValue(expression);
            if (constant.HasValue && constant.Value is string value &&
                value.IndexOf("text/event-stream", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsManualResponseWrite(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return false;
        }

        var containingType = method.ContainingType.ToDisplayString();
        return method.Name == "WriteAsync" && containingType == "Microsoft.AspNetCore.Http.HttpResponseWritingExtensions" ||
            method.Name == "FlushAsync" && InheritsFromStream(method.ContainingType);
    }

    private static bool InheritsFromStream(INamedTypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == "System.IO.Stream")
            {
                return true;
            }
        }

        return false;
    }
}
