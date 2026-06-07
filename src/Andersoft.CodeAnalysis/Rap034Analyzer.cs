using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap034Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP034",
        "Maintainability index is below the allowed minimum",
        "{0} has a maintainability index of {1}, in Visual Studio's {2} band (green requires {3}+) — reduce its size, branching, and token density (the index combines lines of code, cyclomatic complexity, and Halstead volume)",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP034");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    /// <summary>
    /// Members must stay in the green band of Visual Studio's code-metrics
    /// scale (20–100). Anything below 20 is treated as red and fails the build.
    /// </summary>
    private const int MaintainabilityGreenFloor = 20;

    private static void AnalyzeMaintainabilityIndex(SyntaxNodeAnalysisContext context)
    {
        if (IsGeneratedFile(context.Node.SyntaxTree.FilePath))
        {
            return;
        }

        // EF Core migrations are scaffolded, not hand-written.
        var normalizedPath = context.Node.SyntaxTree.FilePath.Replace('\\', '/');
        if (normalizedPath.Contains("/Migrations/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var (body, identifier, displayName) = context.Node switch
        {
            MethodDeclarationSyntax method => ((SyntaxNode?)method.Body ?? method.ExpressionBody, method.Identifier, $"Method '{method.Identifier.ValueText}'"),
            ConstructorDeclarationSyntax ctor => ((SyntaxNode?)ctor.Body ?? ctor.ExpressionBody, ctor.Identifier, $"Constructor '{ctor.Identifier.ValueText}'"),
            LocalFunctionStatementSyntax localFunction => ((SyntaxNode?)localFunction.Body ?? localFunction.ExpressionBody, localFunction.Identifier, $"Local function '{localFunction.Identifier.ValueText}'"),
            AccessorDeclarationSyntax accessor => ((SyntaxNode?)accessor.Body ?? accessor.ExpressionBody, accessor.Keyword, $"Accessor '{accessor.Keyword.ValueText}'"),
            _ => (null, default, string.Empty),
        };

        if (body is null)
        {
            return;
        }

        var maintainabilityIndex = CalculateMaintainabilityIndex(body);
        if (maintainabilityIndex >= MaintainabilityGreenFloor)
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap034Analyzer.Rule,
            identifier.GetLocation(),
            displayName,
            maintainabilityIndex,
            "red",
            MaintainabilityGreenFloor));
    }

    /// <summary>
    /// Visual Studio's composite formula, rescaled to 0–100:
    /// MAX(0, (171 - 5.2*ln(HalsteadVolume) - 0.23*CyclomaticComplexity - 16.2*ln(LinesOfCode)) * 100 / 171).
    /// Reuses the RAP031 cyclomatic walker and the RAP033 Halstead token
    /// classification for the component metrics.
    /// </summary>
    private static int CalculateMaintainabilityIndex(SyntaxNode body)
    {
        var (volume, _, _) = CalculateHalsteadMetrics(body);
        var cyclomatic = CalculateCyclomaticComplexity(body);

        var lineSpan = body.GetLocation().GetLineSpan();
        var linesOfCode = lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1;

        if (volume <= 0 || linesOfCode <= 0)
        {
            return 100;
        }

        var raw = 171
            - (5.2 * Math.Log(volume))
            - (0.23 * cyclomatic)
            - (16.2 * Math.Log(linesOfCode));

        return (int)Math.Max(0, Math.Round(raw * 100 / 171));
    }
}
