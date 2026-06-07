using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap038Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP038",
        "Refused bequest — member stubbed with a throw",
        "{0} immediately throws {1} — a refused bequest; restructure the inheritance or interface so implementers are not forced to stub members they do not support",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP038");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeRefusedBequest(SyntaxNodeAnalysisContext context)
    {
        if (IsGeneratedFile(context.Node.SyntaxTree.FilePath))
        {
            return;
        }

        var (statements, expressionBody, identifier, displayName) = context.Node switch
        {
            MethodDeclarationSyntax method => (method.Body?.Statements, method.ExpressionBody, method.Identifier, $"Method '{method.Identifier.ValueText}'"),
            AccessorDeclarationSyntax accessor => (accessor.Body?.Statements, accessor.ExpressionBody, accessor.Keyword, $"Accessor '{accessor.Keyword.ValueText}' of '{accessor.FirstAncestorOrSelf<PropertyDeclarationSyntax>()?.Identifier.ValueText}'"),
            _ => (null, null, default, string.Empty),
        };

        var thrownExpression = (statements, expressionBody) switch
        {
            ({ Count: 1 } body, _) when body[0] is ThrowStatementSyntax throwStatement => throwStatement.Expression,
            (_, { Expression: ThrowExpressionSyntax throwExpression }) => throwExpression.Expression,
            _ => null,
        };

        if (thrownExpression is not ObjectCreationExpressionSyntax objectCreation)
        {
            return;
        }

        var typeName = objectCreation.Type.ToString();
        var shortName = typeName.Substring(typeName.LastIndexOf('.') + 1);
        if (shortName is not ("NotSupportedException" or "NotImplementedException"))
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap038Analyzer.Rule,
            identifier.GetLocation(),
            displayName,
            shortName));
    }
}
