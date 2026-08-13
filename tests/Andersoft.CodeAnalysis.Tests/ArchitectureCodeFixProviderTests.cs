using Andersoft.CodeAnalysis;
using Xunit;

namespace Andersoft.CodeAnalysis.Tests;

public sealed class ArchitectureCodeFixProviderTests
{
    private readonly ArchitectureCodeFixProvider codeFixProvider = new();

    [Fact]
    public async Task Rap016PassesEnclosingCancellationToken()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;

            sealed class Example
            {
                Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

                public Task RunAsync(CancellationToken cancellationToken)
                {
                    return SaveAsync();
                }
            }
            """;

        var fixedSource = await AnalyzerTestHarness.ApplyFirstFixAsync(source, new Rap016Analyzer(), codeFixProvider);

        Assert.Contains("SaveAsync(cancellationToken: cancellationToken)", fixedSource);
    }

    [Fact]
    public async Task Rap028PassesInScopeCancellationToken()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;

            sealed class Example
            {
                Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

                Task RunAsync(CancellationToken token)
                {
                    return SaveAsync();
                }
            }
            """;

        var fixedSource = await AnalyzerTestHarness.ApplyFirstFixAsync(source, new Rap028Analyzer(), codeFixProvider);

        Assert.Contains("SaveAsync(cancellationToken: token)", fixedSource);
    }

    [Fact]
    public async Task Rap028UsesNoneWhenNoTokenIsInScope()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;

            sealed class Example
            {
                Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

                Task RunAsync()
                {
                    return SaveAsync();
                }
            }
            """;

        var fixedSource = await AnalyzerTestHarness.ApplyFirstFixAsync(source, new Rap028Analyzer(), codeFixProvider);

        Assert.Contains("SaveAsync(cancellationToken: global::System.Threading.CancellationToken.None)", fixedSource);
    }

    [Fact]
    public async Task Rap030RemovesDefaultValue()
    {
        const string source = """
            using System.Threading;

            sealed class Example
            {
                void Run(CancellationToken cancellationToken = default)
                {
                }
            }
            """;

        var fixedSource = await AnalyzerTestHarness.ApplyFirstFixAsync(source, new Rap030Analyzer(), codeFixProvider);

        Assert.Contains("void Run(CancellationToken cancellationToken)", fixedSource);
        Assert.DoesNotContain("= default", fixedSource);
    }

    [Fact]
    public async Task Rap062WrapsIfBody()
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

        var fixedSource = await AnalyzerTestHarness.ApplyFirstFixAsync(source, new Rap062Analyzer(), codeFixProvider);

        Assert.Contains("if (condition)\n        {\n            Run(false);\n        }", fixedSource);
    }

    [Fact]
    public async Task Rap062ConvertsExpressionBodiedMethod()
    {
        const string source = """
            sealed class Example
            {
                int Value() => 1;
            }
            """;

        var fixedSource = await AnalyzerTestHarness.ApplyFirstFixAsync(source, new Rap062Analyzer(), codeFixProvider);

        Assert.Contains("int Value()\n    {\n        return 1;\n    }", fixedSource);
    }

    [Fact]
    public async Task Rap062ConvertsAsyncTaskMethodWithoutReturnValue()
    {
        const string source = """
            using System.Threading.Tasks;

            sealed class Example
            {
                async Task RunAsync() => await Task.Delay(1);
            }
            """;

        var fixedSource = await AnalyzerTestHarness.ApplyFirstFixAsync(source, new Rap062Analyzer(), codeFixProvider);

        Assert.Contains("async Task RunAsync()\n    {\n        await Task.Delay(1);\n    }", fixedSource);
        Assert.DoesNotContain("return await", fixedSource);
    }

    [Fact]
    public async Task Rap062MovesBracesToSeparateLines()
    {
        const string source = "sealed class Example { }";

        var fixedSource = await AnalyzerTestHarness.ApplyFirstFixAsync(source, new Rap062Analyzer(), codeFixProvider);

        Assert.Equal("sealed class Example \n{\n }", fixedSource);
    }
}
