using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap026Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP026",
        "Null-forgiving operator without justification comment",
        "Null-forgiving operator (!) in {0} requires a justification comment (e.g. // EF entity, // boundary adapter, // serialization)",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP026");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static readonly string[] JustificationKeywords =
    {
        "justification", "boundary", "adapter", "ef ", "ef core", "entity",
        "serialization", "serializer", "migration", "[NotMapped]", "activator",
        "atomic", "exchange", "framework", "infrastructure", "dbcontext",
    };

    private static void AnalyzeRap026NullForgivingWithoutJustification(
        SyntaxNodeAnalysisContext context,
        PostfixUnaryExpressionSyntax postfixExpression,
        string containingNamespace)
    {
        var leadingTrivia = postfixExpression.GetLeadingTrivia();
        var hasComment = HasJustificationComment(leadingTrivia);

        if (!hasComment)
        {
            var parentStatement = postfixExpression.AncestorsAndSelf()
                .OfType<StatementSyntax>()
                .FirstOrDefault();
            if (parentStatement is not null)
            {
                hasComment = HasJustificationComment(parentStatement.GetLeadingTrivia());
            }
        }

        if (!hasComment)
        {
            var parentMember = postfixExpression.AncestorsAndSelf()
                .FirstOrDefault(n => n is MemberDeclarationSyntax);
            if (parentMember is not null)
            {
                hasComment = HasJustificationComment(parentMember.GetLeadingTrivia());
            }
        }

        if (hasComment)
        {
            return;
        }

        var layer = IsDomainNamespace(containingNamespace) ? "Domain" : "Application";

        Report(context, Diagnostic.Create(
            Rap026Analyzer.Rule,
            postfixExpression.GetLocation(),
            layer));
    }

    private static bool HasJustificationComment(Microsoft.CodeAnalysis.SyntaxTriviaList trivia)
    {
        foreach (var t in trivia)
        {
            if (!t.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SingleLineCommentTrivia) &&
                !t.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.MultiLineCommentTrivia))
            {
                continue;
            }

            var text = t.ToFullString().ToLowerInvariant();
            foreach (var keyword in JustificationKeywords)
            {
                if (text.Contains(keyword))
                {
                    return true;
                }
            }

            if (text.IndexOf("null-forgiving", System.StringComparison.Ordinal) >= 0 ||
                text.IndexOf("suppress nullable", System.StringComparison.Ordinal) >= 0 ||
                text.IndexOf("suppresswarning", System.StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
