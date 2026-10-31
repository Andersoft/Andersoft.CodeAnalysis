using Microsoft.CodeAnalysis;
using Xunit;

namespace Andersoft.CodeAnalysis.Tests;

public sealed class JsonContractAnalyzerTests
{
    [Theory]
    [InlineData("System.Text.Json.Nodes.JsonObject")]
    [InlineData("System.Text.Json.Nodes.JsonNode")]
    [InlineData("System.Text.Json.Nodes.JsonArray")]
    [InlineData("System.Text.Json.Nodes.JsonValue")]
    [InlineData("System.Text.Json.JsonElement")]
    [InlineData("System.Text.Json.JsonDocument")]
    [InlineData("System.Text.Json.JsonProperty")]
    [InlineData("System.Text.Json.JsonElement?")]
    [InlineData("System.Text.Json.Nodes.JsonObject[]")]
    [InlineData("System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<System.Text.Json.Nodes.JsonObject>>")]
    [InlineData("System.Threading.Tasks.Task<System.Text.Json.JsonElement>")]
    [InlineData("(int Count, System.Text.Json.Nodes.JsonObject State)")]
    [InlineData("System.Func<System.Text.Json.Nodes.JsonObject>")]
    public async Task JsonDomParametersAreBuildErrors(string type)
    {
        var source = $$"""
            class RoomFlash
            {
                private void Validate({{type}} state) { }
            }
            """;

        var diagnostic = Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap063Analyzer()));

        Assert.Equal("RAP063", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.True(diagnostic.Descriptor.IsEnabledByDefault);
        Assert.Contains("state", diagnostic.GetMessage());
        Assert.Contains("concrete class or record", diagnostic.GetMessage());
        Assert.Equal(type, source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
    }

    [Theory]
    [InlineData("class Sample(JsonObject state) { }")]
    [InlineData("record Sample(JsonObject State);")]
    [InlineData("class Sample { public Sample(JsonObject state) { } }")]
    [InlineData("interface Sample { JsonObject Read(); }")]
    [InlineData("class Sample { public Task<JsonObject> Read() => null; }")]
    [InlineData("class Sample { public JsonObject State { get; set; } }")]
    [InlineData("class Sample { private JsonObject state; }")]
    [InlineData("class Sample { private JsonObject first, second; }")]
    [InlineData("class Sample { public JsonObject this[int index] => null; }")]
    [InlineData("delegate JsonObject ReadState();")]
    [InlineData("class Sample { public event Action<JsonObject> Changed; }")]
    [InlineData("class Sample { public event Action<JsonObject> Changed { add { } remove { } } }")]
    [InlineData("class Sample { void Run() { void Apply(JsonObject state) { } } }")]
    [InlineData("class Sample { void Run() { JsonObject Read() => null; } }")]
    [InlineData("class Sample { void Run() { var apply = (JsonObject state) => { }; } }")]
    [InlineData("class Sample { public static implicit operator JsonObject(Sample sample) => null; }")]
    public async Task DeclarationContractsAreFlaggedOnce(string declaration)
    {
        var source = "using System; using System.Threading.Tasks; using System.Text.Json.Nodes; " + declaration;

        Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap063Analyzer()));
    }

    [Fact]
    public async Task AliasesResolveToTheJsonDomType()
    {
        var source = """
            using Payload = System.Text.Json.Nodes.JsonObject;
            class Sample { void Apply(Payload state) { } }
            """;

        var diagnostic = Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap063Analyzer()));

        Assert.Contains("JsonObject", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("Newtonsoft.Json.Linq.JToken")]
    [InlineData("Newtonsoft.Json.Linq.JObject")]
    [InlineData("Newtonsoft.Json.Linq.JArray")]
    [InlineData("CustomObject")]
    public async Task NewtonsoftDomAndDerivedTypesAreFlagged(string type)
    {
        var source = $$"""
            namespace Newtonsoft.Json.Linq
            {
                public class JToken { }
                public class JObject : JToken { }
                public class JArray : JToken { }
            }
            class CustomObject : Newtonsoft.Json.Linq.JObject { }
            class Sample { void Apply({{type}} state) { } }
            """;

        Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap063Analyzer()));
    }

    [Fact]
    public async Task ConcreteModelsAndLocalParsingAreAllowed()
    {
        var source = """
            using System.Text.Json;
            using System.Text.Json.Nodes;
            record RoomState(int Remaining);
            class Sample
            {
                private RoomState state;
                public RoomState Parse(string json)
                {
                    using JsonDocument document = JsonDocument.Parse(json);
                    JsonElement root = document.RootElement;
                    JsonObject local = JsonNode.Parse(json).AsObject();
                    return new RoomState(root.GetProperty("remaining").GetInt32());
                }
                public void Apply(RoomState value) { state = value; }
            }
            """;

        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap063Analyzer()));
    }

    [Fact]
    public async Task UnrelatedTypesWithJsonNamesAreAllowed()
    {
        var source = """
            class JsonObject { }
            class JsonElement { }
            class JObject { }
            class Sample
            {
                public JsonObject Convert(JsonElement value, JObject other) => new();
            }
            """;

        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap063Analyzer()));
    }

    [Fact]
    public async Task GeneratedContractsAreIgnored()
    {
        var source = """
            // <auto-generated/>
            class Generated { public void Apply(System.Text.Json.Nodes.JsonObject state) { } }
            """;

        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap063Analyzer()));
    }
}
