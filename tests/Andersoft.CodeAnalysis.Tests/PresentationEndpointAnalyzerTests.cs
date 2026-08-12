using Andersoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Andersoft.CodeAnalysis.Tests;

public sealed class PresentationEndpointAnalyzerTests
{
    [Theory]
    [MemberData(nameof(Violations))]
    public async Task PresentationViolationIsFlagged(string expectedId, string source, DiagnosticAnalyzer analyzer)
    {
        var diagnostic = Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, analyzer));
        Assert.Equal(expectedId, diagnostic.Id);
    }

    public static TheoryData<string, string, DiagnosticAnalyzer> Violations => new()
    {
        { "RAP053", Sources.Prelude + """
            namespace Example.Presentation { sealed class FamilyEndpoint {
              public void DefineEndpoint(Routes app) { app.MapGet("mine", Get); }
              IResult Get() => Results.Created($"/families/{42}", new object());
            }}
            """, new Rap053Analyzer() },
        { "RAP054", Sources.Prelude + """
            namespace Example.Presentation { sealed class FamilyEndpoint {
              public void DefineEndpoint(Routes app) { app.MapGet("mine", Get); }
              IResult Get(System.Security.Claims.ClaimsPrincipal user, CurrentUserAccessor currentUser) => Results.Ok();
            } sealed class CurrentUserAccessor { } }
            """, new Rap054Analyzer() },
        { "RAP055", Sources.Prelude + """
            namespace Example.Presentation { sealed class FamilyEndpoint {
              public void DefineEndpoint(Routes app) { app.MapGet("mine", Get); }
              IResult Get() => Results.Ok();
            }}
            """, new Rap055Analyzer() },
        { "RAP056", Sources.Prelude + """
            namespace Example.Presentation { sealed class FamilyEndpoint {
              public void DefineEndpoint(Routes app) { app.MapGet("mine", Get); }
              IResult Get() => Map(true); IResult Map(bool created) => created ? Results.Created("mine", new object()) : Results.Ok();
            }}
            """, new Rap056Analyzer() },
        { "RAP057", Sources.Prelude + """
            namespace Example.Presentation { sealed class FamilyEndpoint {
              public void DefineEndpoint(Routes app) { app.MapGet("events", Stream); }
              async System.Threading.Tasks.Task Stream() { while (true) {
                var available = System.Threading.Tasks.Task.Delay(10); var heartbeat = System.Threading.Tasks.Task.Delay(10);
                if (await System.Threading.Tasks.Task.WhenAny(available, heartbeat) == heartbeat) continue;
                await available; break;
              }}
            }}
            """, new Rap057Analyzer() },
        { "RAP058", Sources.Prelude + """
            namespace Example.Presentation { sealed class HealthEndpoint {
              public void DefineEndpoint(Routes app) { app.MapGet("health", Get); }
              IResult Get() => Results.Text("{\"status\":\"healthy\"}", "application/json");
            }}
            """, new Rap058Analyzer() },
        { "RAP059", Sources.Prelude + """
            namespace Example.Presentation { sealed class AuthEndpoint {
              public void DefineEndpoint(Routes app) { app.MapGet("session", Get); }
              System.DateTime Get() => System.DateTime.SpecifyKind(default, System.DateTimeKind.Utc);
            }}
            """, new Rap059Analyzer() },
        { "RAP060", Sources.Prelude + """
            namespace Example.Presentation { sealed class FamilyEndpoint {
              public void DefineEndpoint(Routes app) { app.MapGet("mine", Get); }
              IResult Get() => Results.Forbid();
            }}
            """, new Rap060Analyzer() },
    };

    [Fact]
    public async Task CreatedLocationMatchingMappedRouteIsAccepted()
    {
        var source = Sources.Prelude + """
            namespace Example.Presentation { sealed class FamilyEndpoint {
              public void DefineEndpoint(Routes app) { app.MapGet("families/{id}", Get); }
              IResult Get() => Results.Created($"/families/{42}", new object());
            }}
            """;
        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap053Analyzer()));
    }

    private static class Sources
    {
        public const string Prelude = """
            global using Microsoft.AspNetCore.Http;
            namespace Microsoft.AspNetCore.Http {
              public interface IResult { }
              public sealed class Result : IResult { }
              public static class Results {
                public static IResult Ok() => new Result();
                public static IResult Created(string path, object value) => new Result();
                public static IResult Text(string value, string mediaType) => new Result();
                public static IResult Forbid() => new Result();
              }
            }
            public sealed class Routes {
              public void MapGet(string route, System.Delegate handler) { }
              public void MapPost(string route, System.Delegate handler) { }
            }
            """;
    }
}
