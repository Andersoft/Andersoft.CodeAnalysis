using Andersoft.CodeAnalysis;
using Xunit;

namespace Andersoft.CodeAnalysis.Tests;

public sealed class EndpointQualityAnalyzerTests
{
    [Fact]
    public async Task EndpointWithTooManyOperationsIsFlagged()
    {
        const string source = """
            namespace Example.Presentation;
            sealed class FamiliesEndpoint
            {
                void DefineEndpoint(Routes app)
                {
                    app.MapGet(); app.MapGet(); app.MapPost(); app.MapPost(); app.MapPut();
                    app.MapPut(); app.MapDelete(); app.MapDelete(); app.MapPatch();
                }
            }
            sealed class Routes
            {
                public void MapGet() { } public void MapPost() { } public void MapPut() { }
                public void MapDelete() { } public void MapPatch() { }
            }
            """;

        var diagnostic = Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap049Analyzer()));
        Assert.Equal("RAP049", diagnostic.Id);
    }

    [Fact]
    public async Task HardCodedClaimsPrincipalRoleIsFlagged()
    {
        const string source = """
            using System.Security.Claims;
            namespace Example.Presentation;
            sealed class Endpoint
            {
                bool CanManage(ClaimsPrincipal user) => user.IsInRole("parental");
            }
            """;

        var diagnostic = Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap050Analyzer()));
        Assert.Equal("RAP050", diagnostic.Id);
    }

    [Fact]
    public async Task CatchAllResultMappingIsFlagged()
    {
        const string source = """
            namespace Example.Presentation;
            abstract record FamilyResult
            {
                public sealed record Success : FamilyResult;
            }
            sealed class Endpoint
            {
                int Map(FamilyResult result) => result switch
                {
                    FamilyResult.Success => 200,
                    _ => 404
                };
            }
            """;

        var diagnostic = Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap051Analyzer()));
        Assert.Equal("RAP051", diagnostic.Id);
    }

    [Fact]
    public async Task ExistingQualityRuleIsDispatched()
    {
        const string source = """
            sealed class Endpoint
            {
                void Handle(int one, int two, int three, int four, int five, int six) { }
            }
            """;

        var diagnostic = Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap036Analyzer()));
        Assert.Equal("RAP036", diagnostic.Id);
    }

    [Fact]
    public async Task ManuallyWrittenServerSentEventsAreFlagged()
    {
        const string source = """
            namespace Microsoft.AspNetCore.Http
            {
                public sealed class HttpContext { public HttpResponse Response { get; } = new(); }
                public sealed class HttpResponse
                {
                    public string ContentType { get; set; } = "";
                }
                public static class HttpResponseWritingExtensions
                {
                    public static System.Threading.Tasks.Task WriteAsync(this HttpResponse response, string value) =>
                        System.Threading.Tasks.Task.CompletedTask;
                }
            }
            namespace Example.Presentation
            {
                using Microsoft.AspNetCore.Http;
                sealed class Endpoint
                {
                    async System.Threading.Tasks.Task Stream(HttpContext context)
                    {
                        context.Response.ContentType = "text/event-stream";
                        await context.Response.WriteAsync("data: {}\n\n");
                    }
                }
            }
            """;

        var diagnostic = Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap052Analyzer()));
        Assert.Equal("RAP052", diagnostic.Id);
    }

    [Fact]
    public async Task OrdinaryResponseWritesAreNotFlaggedAsServerSentEvents()
    {
        const string source = """
            namespace Microsoft.AspNetCore.Http
            {
                public sealed class HttpContext { public HttpResponse Response { get; } = new(); }
                public sealed class HttpResponse { }
                public static class HttpResponseWritingExtensions
                {
                    public static System.Threading.Tasks.Task WriteAsync(this HttpResponse response, string value) =>
                        System.Threading.Tasks.Task.CompletedTask;
                }
            }
            namespace Example.Presentation
            {
                using Microsoft.AspNetCore.Http;
                sealed class Endpoint
                {
                    System.Threading.Tasks.Task Write(HttpContext context) => context.Response.WriteAsync("ok");
                }
            }
            """;

        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap052Analyzer()));
    }
}
