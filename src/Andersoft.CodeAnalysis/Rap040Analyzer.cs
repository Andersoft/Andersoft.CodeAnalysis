using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap040Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP040",
        "Message chain — navigating too deep through object structure",
        "'{0}' chains through {1} properties — a message chain couples this code to the whole navigation path; ask the nearest object for what you need (Law of Demeter)",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP040");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    /// <summary>
    /// Maximum number of consecutive instance property/field hops. Method
    /// invocations break the chain, so fluent APIs (LINQ, builders) are
    /// unaffected.
    /// </summary>
    private const int MaxMessageChainLength = 3;

    private static void AnalyzeMessageChain(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        if (IsGeneratedFile(memberAccess.SyntaxTree.FilePath))
        {
            return;
        }

        // Only measure from the outermost link so sub-chains are not re-reported.
        if (memberAccess.Parent is MemberAccessExpressionSyntax outer && outer.Expression == memberAccess)
        {
            return;
        }

        var length = 0;
        SyntaxNode current = memberAccess;
        while (current is MemberAccessExpressionSyntax link)
        {
            var symbol = context.SemanticModel.GetSymbolInfo(link).Symbol;
            if (symbol is IPropertySymbol { IsStatic: false } or IFieldSymbol { IsStatic: false })
            {
                length++;
                current = link.Expression;
            }
            else
            {
                break;
            }
        }

        if (length <= MaxMessageChainLength)
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap040Analyzer.Rule,
            memberAccess.GetLocation(),
            memberAccess.ToString(),
            length));
    }
}
