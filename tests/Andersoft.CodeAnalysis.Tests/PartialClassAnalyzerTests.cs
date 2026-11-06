using Microsoft.CodeAnalysis;
using Xunit;

namespace Andersoft.CodeAnalysis.Tests;

public sealed class PartialClassAnalyzerTests
{
    [Theory]
    [InlineData("public partial class Handler { }")]
    [InlineData("public sealed partial class Handler { }")]
    [InlineData("public abstract partial class Handler { }")]
    [InlineData("public static partial class Handler { }")]
    [InlineData("public partial class Handler<T> { }")]
    [InlineData("public class Outer { private partial class Handler { } }")]
    public async Task HandWrittenPartialClassesAreErrors(string source)
    {
        var diagnostic = Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap066Analyzer()));

        Assert.Equal("RAP066", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Handler", source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
        Assert.Contains("distinct responsibilities", diagnostic.GetMessage());
    }

    [Fact]
    public async Task EveryPartialDeclarationIsFlaggedEvenWhenEachPartIsSmall()
    {
        const string source = """
            public sealed partial class ChangeRoomCommandHandler { private void Control() { } }
            public sealed partial class ChangeRoomCommandHandler { private void Output() { } }
            public sealed partial class ChangeRoomCommandHandler { private void Permissions() { } }
            """;

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(source, new Rap066Analyzer());

        Assert.Equal(3, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal("RAP066", diagnostic.Id));
    }

    [Theory]
    [InlineData("public sealed class Handler { }")]
    [InlineData("public partial struct Value { }")]
    [InlineData("public partial interface IHandler { }")]
    [InlineData("[System.CodeDom.Compiler.GeneratedCode(\"Generator\", \"1.0\")] public partial class Handler { }")]
    public async Task OrdinaryClassesOtherTypeKindsAndGeneratedClassesAreAllowed(string source)
    {
        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap066Analyzer()));
    }

    [Fact]
    public async Task GeneratorRequiredDeclarationsCanBeExplicitlySuppressed()
    {
        const string source = """
            #pragma warning disable RAP066
            public partial class SerializerContext { }
            #pragma warning restore RAP066
            public partial class Handler { }
            """;

        var diagnostic = Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap066Analyzer()));

        Assert.Equal("Handler", source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
    }
}
