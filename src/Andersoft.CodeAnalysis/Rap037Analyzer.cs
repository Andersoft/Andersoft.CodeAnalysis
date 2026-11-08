using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap037Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP037",
        "Large class",
        "Class '{0}' has {1} members across {2} lines (max {3} members / {4} lines) — a bloater; split its responsibilities into smaller collaborators",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP037");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private const int MaxClassMemberCount = 25;
    private const int MaxClassLineCount = 400;

    private static void AnalyzeLargeClass(SyntaxNodeAnalysisContext context)
        => AnalyzeClassSize(context, aggregatesOnly: false);

    private static void AnalyzeLargeAggregate(SyntaxNodeAnalysisContext context)
        => AnalyzeClassSize(context, aggregatesOnly: true);

    private static void AnalyzeClassSize(SyntaxNodeAnalysisContext context, bool aggregatesOnly)
    {
        if (context.Node is not ClassDeclarationSyntax classDeclaration)
        {
            return;
        }

        var filePath = classDeclaration.SyntaxTree.FilePath;
        if (IsGeneratedFile(filePath) ||
            filePath.Replace('\\', '/').Contains("/Migrations/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Partial classes are measured per part — each part should stay small.
        var memberCount = classDeclaration.Members.Count;
        var lineSpan = classDeclaration.GetLocation().GetLineSpan();
        var lineCount = lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1;

        if (memberCount <= MaxClassMemberCount && lineCount <= MaxClassLineCount)
        {
            return;
        }

        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration, context.CancellationToken) as INamedTypeSymbol;
        var inheritsAggregate = false;
        for (var baseType = classSymbol?.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType.Name == "Aggregate" && baseType.Arity == 1 &&
                baseType.IsAbstract && baseType.DeclaredAccessibility == Accessibility.Public)
            {
                inheritsAggregate = true;
                break;
            }
        }

        if (inheritsAggregate != aggregatesOnly)
        {
            return;
        }

        Report(context, Diagnostic.Create(
            aggregatesOnly ? Rap068Analyzer.Rule : Rap037Analyzer.Rule,
            classDeclaration.Identifier.GetLocation(),
            classDeclaration.Identifier.ValueText,
            memberCount,
            lineCount,
            MaxClassMemberCount,
            MaxClassLineCount));
    }
}
