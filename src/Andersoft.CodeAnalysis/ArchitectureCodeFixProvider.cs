using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace Andersoft.CodeAnalysis;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ArchitectureCodeFixProvider)), Shared]
public sealed class ArchitectureCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("RAP016", "RAP028", "RAP030", "RAP062");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            switch (diagnostic.Id)
            {
                case "RAP016":
                case "RAP028":
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            "Pass CancellationToken",
                            cancellationToken => AddCancellationTokenAsync(context.Document, diagnostic, cancellationToken),
                            $"{diagnostic.Id}:PassCancellationToken"),
                        diagnostic);
                    break;
                case "RAP030":
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            "Remove CancellationToken default value",
                            cancellationToken => RemoveCancellationTokenDefaultAsync(context.Document, diagnostic, cancellationToken),
                            "RAP030:RemoveDefault"),
                        diagnostic);
                    break;
                case "RAP062":
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            "Use required block formatting",
                            cancellationToken => FixBlockFormattingAsync(context.Document, diagnostic, cancellationToken),
                            "RAP062:BlockFormatting"),
                        diagnostic);
                    break;
            }
        }
    }

    private static async Task<Document> AddCancellationTokenAsync(
        Document document,
        Diagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root?.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<InvocationExpressionSyntax>() is not { } invocation ||
            semanticModel?.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method)
        {
            return document;
        }

        var tokenParameter = method.Parameters.FirstOrDefault(parameter =>
            parameter.Type.ToDisplayString() == "System.Threading.CancellationToken");
        if (tokenParameter is null)
        {
            return document;
        }

        var tokenExpression = FindCancellationTokenExpression(semanticModel, invocation.SpanStart, diagnostic.Id);
        var argument = SyntaxFactory.Argument(tokenExpression)
            .WithNameColon(SyntaxFactory.NameColon(tokenParameter.Name));
        var updated = invocation.WithArgumentList(
                invocation.ArgumentList.WithArguments(invocation.ArgumentList.Arguments.Add(argument)))
            .WithAdditionalAnnotations(Formatter.Annotation);

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, updated));
    }

    private static ExpressionSyntax FindCancellationTokenExpression(
        SemanticModel semanticModel,
        int position,
        string diagnosticId)
    {
        var token = semanticModel.LookupSymbols(position)
            .FirstOrDefault(symbol => symbol is IParameterSymbol or ILocalSymbol &&
                GetSymbolType(symbol)?.ToDisplayString() == "System.Threading.CancellationToken");

        if (token is not null)
        {
            return SyntaxFactory.IdentifierName(token.Name);
        }

        // RAP016 is only emitted when the enclosing method has a token, but retain
        // a valid fallback for incomplete code and for RAP028 call sites.
        _ = diagnosticId;
        return SyntaxFactory.ParseExpression("global::System.Threading.CancellationToken.None");
    }

    private static ITypeSymbol? GetSymbolType(ISymbol symbol) => symbol switch
    {
        IParameterSymbol parameter => parameter.Type,
        ILocalSymbol local => local.Type,
        _ => null,
    };

    private static async Task<Document> RemoveCancellationTokenDefaultAsync(
        Document document,
        Diagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root?.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<ParameterSyntax>() is not { Default: not null } parameter)
        {
            return document;
        }

        var updated = parameter.WithDefault(null).WithAdditionalAnnotations(Formatter.Annotation);
        return document.WithSyntaxRoot(root.ReplaceNode(parameter, updated));
    }

    private static async Task<Document> FixBlockFormattingAsync(
        Document document,
        Diagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
        if (node.FirstAncestorOrSelf<IfStatementSyntax>() is { Statement: not BlockSyntax } ifStatement)
        {
            var statement = ifStatement.Statement;
            var block = SyntaxFactory.Block(statement.WithoutLeadingTrivia())
                .WithLeadingTrivia(statement.GetLeadingTrivia())
                .WithAdditionalAnnotations(Formatter.Annotation);
            return document.WithSyntaxRoot(root.ReplaceNode(ifStatement.Statement, block));
        }

        if (node.FirstAncestorOrSelf<MethodDeclarationSyntax>() is { ExpressionBody: not null } method)
        {
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var returnsValue = semanticModel?.GetDeclaredSymbol(method, cancellationToken) is IMethodSymbol symbol &&
                ExpressionBodyReturnsValue(symbol, method.Modifiers.Any(SyntaxKind.AsyncKeyword));
            var body = CreateBlock(method.ExpressionBody.Expression, returnsValue)
                .WithAdditionalAnnotations(Formatter.Annotation);
            var updated = method.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body);
            return document.WithSyntaxRoot(root.ReplaceNode(method, updated));
        }

        if (node.FirstAncestorOrSelf<LambdaExpressionSyntax>() is { Body: ExpressionSyntax expression } lambda)
        {
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var invokeMethod = (semanticModel?.GetTypeInfo(lambda, cancellationToken).ConvertedType as INamedTypeSymbol)?.DelegateInvokeMethod;
            var returnsValue = invokeMethod is not null &&
                ExpressionBodyReturnsValue(invokeMethod, lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword));
            var body = CreateBlock(expression, returnsValue)
                .WithAdditionalAnnotations(Formatter.Annotation);
            return document.WithSyntaxRoot(root.ReplaceNode(lambda, lambda.WithBody(body)));
        }

        return await PutBracesOnSeparateLinesAsync(document, cancellationToken).ConfigureAwait(false);
    }

    private static bool ExpressionBodyReturnsValue(IMethodSymbol method, bool isAsync) =>
        !method.ReturnsVoid &&
        !(isAsync && method.ReturnType is INamedTypeSymbol { Name: "Task", Arity: 0 });

    private static BlockSyntax CreateBlock(ExpressionSyntax expression, bool returnsValue)
    {
        StatementSyntax statement = expression switch
        {
            ThrowExpressionSyntax throwExpression => SyntaxFactory.ThrowStatement(throwExpression.Expression),
            _ when returnsValue => SyntaxFactory.ReturnStatement(expression),
            _ => SyntaxFactory.ExpressionStatement(expression),
        };
        return SyntaxFactory.Block(statement);
    }

    private static async Task<Document> PutBracesOnSeparateLinesAsync(
        Document document,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var changes = root.DescendantTokens()
            .Where(token =>
                (token.IsKind(SyntaxKind.OpenBraceToken) || token.IsKind(SyntaxKind.CloseBraceToken)) &&
                token.Parent is not AccessorListSyntax and not InterpolationSyntax)
            .SelectMany(token => GetBraceLineChanges(text, token))
            .OrderByDescending(change => change.Span.Start)
            .ToArray();

        return changes.Length == 0 ? document : document.WithText(text.WithChanges(changes));
    }

    private static ImmutableArray<TextChange> GetBraceLineChanges(SourceText text, SyntaxToken brace)
    {
        var line = text.Lines.GetLineFromPosition(brace.SpanStart);
        var prefix = text.ToString(TextSpan.FromBounds(line.Start, brace.SpanStart));
        var suffix = text.ToString(TextSpan.FromBounds(brace.Span.End, line.End));
        var indentation = new string(prefix.TakeWhile(char.IsWhiteSpace).ToArray());
        var builder = ImmutableArray.CreateBuilder<TextChange>(2);

        var previousToken = brace.GetPreviousToken();
        var adjacentEmptyBlock = brace.IsKind(SyntaxKind.CloseBraceToken) &&
            previousToken.IsKind(SyntaxKind.OpenBraceToken) &&
            string.IsNullOrWhiteSpace(text.ToString(TextSpan.FromBounds(previousToken.Span.End, brace.SpanStart)));

        if (!string.IsNullOrWhiteSpace(prefix) && !adjacentEmptyBlock)
        {
            builder.Add(new TextChange(new TextSpan(brace.SpanStart, 0), "\n" + indentation));
        }

        var allowsTrailingText = brace.IsKind(SyntaxKind.CloseBraceToken) &&
            brace.Parent is BlockSyntax { Parent: LambdaExpressionSyntax } or SwitchExpressionSyntax;
        if (!string.IsNullOrWhiteSpace(suffix) && !allowsTrailingText)
        {
            builder.Add(new TextChange(new TextSpan(brace.Span.End, 0), "\n" + indentation));
        }

        return builder.ToImmutable();
    }
}
