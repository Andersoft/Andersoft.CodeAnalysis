using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap033Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP033",
        "Halstead complexity exceeds the allowed maximum",
        "{0} is too dense: Halstead volume {1} (max {2}), difficulty {3} (max {4}), effort {5} (max {6}) — split the member up or introduce explaining variables/methods to reduce the vocabulary the reader must absorb",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP033");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    /// <summary>
    /// Volume = N * log2(n): how much information the reader has to absorb.
    /// </summary>
    private const double MaxHalsteadVolume = 1000;

    /// <summary>
    /// Difficulty = (n1 / 2) * (N2 / n2): how error-prone the code is likely to be.
    /// </summary>
    private const double MaxHalsteadDifficulty = 30;

    /// <summary>
    /// Effort = Difficulty * Volume: how long the code should mentally take
    /// to write or understand.
    /// </summary>
    private const double MaxHalsteadEffort = 50000;

    private static void AnalyzeHalsteadComplexity(SyntaxNodeAnalysisContext context)
    {
        if (IsGeneratedFile(context.Node.SyntaxTree.FilePath))
        {
            return;
        }

        // EF Core migrations are scaffolded, not hand-written — their token
        // density says nothing about maintainability.
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

        var (volume, difficulty, effort) = CalculateHalsteadMetrics(body);

        if (volume <= MaxHalsteadVolume &&
            difficulty <= MaxHalsteadDifficulty &&
            effort <= MaxHalsteadEffort)
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap033Analyzer.Rule,
            identifier.GetLocation(),
            displayName,
            Math.Round(volume),
            MaxHalsteadVolume,
            Math.Round(difficulty),
            MaxHalsteadDifficulty,
            Math.Round(effort),
            MaxHalsteadEffort));
    }

    /// <summary>
    /// Classifies every token in the body as an operator or an operand and
    /// computes the Halstead measures from the four base counts:
    /// n1/n2 = distinct operators/operands, N1/N2 = total operators/operands.
    /// Operands are identifiers and literals (including true/false/null);
    /// operators are keywords and operator/punctuation tokens. Pure structural
    /// delimiters (braces, semicolons, commas) are ignored because they carry
    /// no information of their own.
    /// </summary>
    private static (double Volume, double Difficulty, double Effort) CalculateHalsteadMetrics(SyntaxNode body)
    {
        var distinctOperators = new HashSet<SyntaxKind>();
        var distinctOperands = new HashSet<string>(StringComparer.Ordinal);
        var totalOperators = 0;
        var totalOperands = 0;

        foreach (var token in body.DescendantTokens())
        {
            var kind = token.Kind();
            switch (kind)
            {
                // Structural delimiters — no informational content
                case SyntaxKind.SemicolonToken:
                case SyntaxKind.CommaToken:
                case SyntaxKind.OpenBraceToken:
                case SyntaxKind.CloseBraceToken:
                case SyntaxKind.EndOfFileToken:
                    continue;

                // Operands: identifiers and literals
                case SyntaxKind.IdentifierToken:
                case SyntaxKind.NumericLiteralToken:
                case SyntaxKind.StringLiteralToken:
                case SyntaxKind.Utf8StringLiteralToken:
                case SyntaxKind.CharacterLiteralToken:
                case SyntaxKind.InterpolatedStringTextToken:
                case SyntaxKind.TrueKeyword:
                case SyntaxKind.FalseKeyword:
                case SyntaxKind.NullKeyword:
                    totalOperands++;
                    distinctOperands.Add(token.Text);
                    continue;
            }

            // Operators: keywords and operator/punctuation tokens
            if (SyntaxFacts.IsKeywordKind(kind) || SyntaxFacts.IsPunctuation(kind))
            {
                totalOperators++;
                distinctOperators.Add(kind);
            }
        }

        var n1 = distinctOperators.Count;
        var n2 = distinctOperands.Count;
        var vocabulary = n1 + n2;
        var length = totalOperators + totalOperands;

        if (vocabulary == 0 || n2 == 0)
        {
            return (0, 0, 0);
        }

        var volume = length * Math.Log(vocabulary, 2);
        var difficulty = n1 / 2.0 * totalOperands / n2;
        var effort = difficulty * volume;

        return (volume, difficulty, effort);
    }
}
