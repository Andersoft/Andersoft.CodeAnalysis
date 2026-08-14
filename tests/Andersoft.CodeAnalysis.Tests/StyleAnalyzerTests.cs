using Andersoft.CodeAnalysis;
using Xunit;

namespace Andersoft.CodeAnalysis.Tests;

public sealed class StyleAnalyzerTests
{
    [Fact]
    public async Task IfStatementWithoutBlockIsFlagged()
    {
        const string source = """
            sealed class Example
            {
                void Run(bool condition)
                {
                    if (condition)
                        Run(false);
                }
            }
            """;

        var diagnostic = Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap062Analyzer()));

        Assert.Equal("RAP062", diagnostic.Id);
        Assert.Equal("If statement body must be enclosed in braces", diagnostic.GetMessage());
    }

    [Fact]
    public async Task BracesSharingLinesAreFlagged()
    {
        const string source = """
            sealed class Example {
                void Run()
                { }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(source, new Rap062Analyzer());

        Assert.Equal(3, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Contains("must be on its own line", diagnostic.GetMessage()));
    }

    [Fact]
    public async Task BlockIfAndBracesOnSeparateLinesAreAccepted()
    {
        const string source = """
            sealed class Example
            {
                void Run(bool condition)
                {
                    if (condition)
                    {
                        Run(false);
                    }
                }
            }
            """;

        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap062Analyzer()));
    }

    [Fact]
    public async Task ExpressionBodiedMethodIsFlagged()
    {
        const string source = """
            sealed class Example
            {
                private static int InvalidProfile() => 1;
            }
            """;

        var diagnostic = Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap062Analyzer()));

        Assert.Equal("Method body must be enclosed in braces", diagnostic.GetMessage());
    }

    [Fact]
    public async Task ExpressionBodiedLambdaOutsideFunctionIsFlagged()
    {
        const string source = """
            using System;

            sealed class Example
            {
                private readonly Func<int> value = () => 1;
            }
            """;

        var diagnostic = Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap062Analyzer()));

        Assert.Contains("unless the lambda is declared within a function", diagnostic.GetMessage());
    }

    [Fact]
    public async Task ExpressionBodiedLocalFunctionAndLambdaAreAccepted()
    {
        const string source = """
            using System;

            sealed class Example
            {
                int Run()
                {
                    int Local() => 1;
                    Func<int> value = () => Local();
                    return value();
                }
            }
            """;

        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap062Analyzer()));
    }

    [Fact]
    public async Task InlineGetterAndSetterBracesAreAccepted()
    {
        const string source = """
            sealed class Example
            {
                public int Position { get; private set; }
            }
            """;

        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap062Analyzer()));
    }

    [Fact]
    public async Task GetterBodyBracesMustStillBeOnSeparateLines()
    {
        const string source = """
            sealed class Example
            {
                public int Position
                {
                    get { return 1; }
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(source, new Rap062Analyzer());

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Contains("must be on its own line", diagnostic.GetMessage()));
    }

    [Fact]
    public async Task LambdaClosingBraceMayShareLineWithInvocationClosingTokens()
    {
        const string source = """
            using System;

            sealed class Example
            {
                void Register(Action<Action> register)
                {
                    register(action =>
                    {
                        action();
                    });
                }
            }
            """;

        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap062Analyzer()));
    }

    [Fact]
    public async Task LambdaClosingBraceMustStillStartItsOwnLine()
    {
        const string source = """
            using System;

            sealed class Example
            {
                void Register(Action<Action> register)
                {
                    register(action =>
                    {
                        action(); });
                }
            }
            """;

        var diagnostic = Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap062Analyzer()));

        Assert.Contains("must be on its own line", diagnostic.GetMessage());
    }

    [Fact]
    public async Task SwitchExpressionClosingBraceMayShareLineWithSemicolon()
    {
        const string source = """
            sealed class Example
            {
                string GetName(int value)
                {
                    return value switch
                    {
                        1 => "one",
                        _ => "other"
                    };
                }
            }
            """;

        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap062Analyzer()));
    }

    [Fact]
    public async Task SwitchExpressionClosingBraceMustStillStartItsOwnLine()
    {
        const string source = """
            sealed class Example
            {
                string GetName(int value)
                {
                    return value switch
                    {
                        1 => "one",
                        _ => "other" };
                }
            }
            """;

        var diagnostic = Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap062Analyzer()));

        Assert.Contains("must be on its own line", diagnostic.GetMessage());
    }

    [Fact]
    public async Task InterpolationBracesMayRemainInline()
    {
        const string source = """
            sealed class Example
            {
                string GetCollectionName<TDocument>(string prefix)
                {
                    return $"{prefix}{typeof(TDocument).Name}";
                }
            }
            """;

        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap062Analyzer()));
    }
}
