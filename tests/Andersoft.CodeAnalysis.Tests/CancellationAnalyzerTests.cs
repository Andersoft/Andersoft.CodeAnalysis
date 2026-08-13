using Andersoft.CodeAnalysis;
using Xunit;

namespace Andersoft.CodeAnalysis.Tests;

public sealed class CancellationAnalyzerTests
{
    [Fact]
    public async Task UnjustifiedNullForgivingOperatorIsFlagged()
    {
        const string source = """
            sealed class Example
            {
                string Value(string? value)
                {
                    return value!;
                }
            }
            """;

        var diagnostic = Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap026Analyzer()));

        Assert.Equal("RAP026", diagnostic.Id);
    }

    [Fact]
    public async Task MissingCancellationTokenArgumentIsFlagged()
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

        var diagnostic = Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap028Analyzer()));

        Assert.Equal("RAP028", diagnostic.Id);
    }

    [Fact]
    public async Task DefaultCancellationTokenIsFlagged()
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

        var diagnostic = Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap030Analyzer()));

        Assert.Equal("RAP030", diagnostic.Id);
    }
}
