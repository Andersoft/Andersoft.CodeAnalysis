using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Andersoft.CodeAnalysis.Tests;

public sealed class ConfigurationAnalyzerTests
{
    private const string Contracts = """
        using Microsoft.Extensions.Configuration;
        using Microsoft.Extensions.Options;
        namespace Microsoft.Extensions.Configuration
        {
            public interface IConfiguration
            {
                string this[string key] { get; set; }
                IConfigurationSection GetSection(string key);
                System.Collections.Generic.IEnumerable<IConfigurationSection> GetChildren();
            }
            public interface IConfigurationSection : IConfiguration { string Value { get; set; } }
            public interface IConfigurationRoot : IConfiguration { }
            public abstract class ConfigurationManager : IConfigurationRoot
            {
                public abstract string this[string key] { get; set; }
                public abstract IConfigurationSection GetSection(string key);
                public abstract System.Collections.Generic.IEnumerable<IConfigurationSection> GetChildren();
            }
            public static class ConfigurationExtensions
            {
                public static T GetValue<T>(this IConfiguration configuration, string key) => default;
                public static T Get<T>(this IConfiguration configuration) => default;
                public static object Get(this IConfiguration configuration, System.Type type) => default;
                public static void Bind(this IConfiguration configuration, object settings) { }
                public static string GetConnectionString(this IConfiguration configuration, string key) => "";
                public static IConfigurationSection GetRequiredSection(this IConfiguration configuration, string key) => default;
                public static System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, string>>
                    AsEnumerable(this IConfiguration configuration) => default;
            }
        }
        namespace Microsoft.Extensions.Options
        {
            public interface IOptions<T> { T Value { get; } }
            public interface IOptionsSnapshot<T> : IOptions<T> { }
            public interface IOptionsMonitor<T> { T CurrentValue { get; } }
        }
        namespace Microsoft.AspNetCore.Mvc
        {
            public sealed class FromServicesAttribute : System.Attribute { }
        }
        namespace Microsoft.AspNetCore.Components
        {
            public sealed class InjectAttribute : System.Attribute { }
        }
        namespace Microsoft.Extensions.DependencyInjection
        {
            public sealed class FromKeyedServicesAttribute(string key) : System.Attribute { }
        }
        namespace Example
        {
            public sealed class Settings { public string Authority { get; set; } }
            public interface ISpecialConfiguration : IConfiguration { }
        }
        """;

    [Theory]
    [InlineData("IConfiguration")]
    [InlineData("IConfigurationSection")]
    [InlineData("IConfigurationRoot")]
    [InlineData("ConfigurationManager")]
    [InlineData("ISpecialConfiguration")]
    [InlineData("IOptions<Settings>")]
    [InlineData("IOptionsSnapshot<Settings>")]
    [InlineData("IOptionsMonitor<Settings>")]
    [InlineData("System.Lazy<IOptions<Settings>>")]
    [InlineData("System.Func<IConfiguration>")]
    [InlineData("System.Collections.Generic.IEnumerable<IConfiguration>")]
    public async Task PrimaryConstructorDependenciesAreErrors(string type)
    {
        var source = Contracts + $$"""
            namespace Example { public sealed class Consumer({{type}} dependency) { } }
            """;
        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal("RAP067", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("dependency", source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
        Assert.Contains("register the POCO as a singleton", diagnostic.GetMessage());
        Assert.Contains("builder.Services.AddOptions<TSettings>().BindConfiguration(\"SectionName\").ValidateDataAnnotations().ValidateOnStart()", diagnostic.GetMessage());
        Assert.Contains("builder.Services.AddSingleton(provider => provider.GetRequiredService<IOptions<TSettings>>().Value)", diagnostic.GetMessage());
        Assert.Contains("inject TSettings directly", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("public class Consumer { public Consumer(IConfiguration dependency) { } }")]
    [InlineData("public record Consumer(IOptions<Settings> Dependency);")]
    [InlineData("public class Consumer<T>(T dependency) where T : IConfiguration { }")]
    [InlineData("public class Consumer<T>(T dependency) where T : IOptions<Settings> { }")]
    [InlineData("public class Consumer { [Microsoft.AspNetCore.Components.Inject] public IConfiguration Dependency { get; set; } }")]
    [InlineData("public class Consumer { public void Handle([Microsoft.AspNetCore.Mvc.FromServices] IOptions<Settings> dependency) { } }")]
    [InlineData("public class Consumer { public void Handle([Microsoft.Extensions.DependencyInjection.FromKeyedServices(\"settings\")] IConfiguration dependency) { } }")]
    [InlineData("public class Consumer(Alias dependency) { }")]
    public async Task OtherInjectionFormsAreErrors(string declaration)
    {
        var source = Contracts + $$"""
            namespace Example
            {
                using Alias = Microsoft.Extensions.Options.IOptions<Settings>;
                {{declaration}}
            }
            """;
        Assert.Single(await AnalyzeAsync(source));
    }

    [Theory]
    [InlineData("configuration[\"Authority\"]")]
    [InlineData("configuration?[\"Authority\"]")]
    [InlineData("configuration.GetValue<string>(\"Authority\")")]
    [InlineData("ConfigurationExtensions.GetValue<string>(configuration, \"Authority\")")]
    [InlineData("configuration.GetConnectionString(\"Database\")")]
    [InlineData("configuration.GetChildren()")]
    [InlineData("configuration.AsEnumerable()")]
    [InlineData("configuration.GetSection(\"Authentication\").Value")]
    [InlineData("configuration.GetSection(\"Authentication\")[\"Authority\"]")]
    [InlineData("configuration.GetSection(\"Timeout\").Get<int>()")]
    [InlineData("configuration.GetSection(\"Authority\").Get<string>()")]
    [InlineData("configuration.GetSection(\"Authority\").Get(typeof(string))")]
    [InlineData("ConfigurationExtensions.Get(configuration, typeof(int))")]
    [InlineData("(System.Func<string, string>)configuration.GetValue<string>")]
    public async Task DirectReadsAreErrorsEvenOutsideConstructors(string expression)
    {
        var source = Contracts + $$"""
            namespace Example
            {
                public static class Consumer
                {
                    public static object Read(IConfiguration configuration) => {{expression}};
                }
            }
            """;
        var diagnostic = Assert.Single(await AnalyzeAsync(source));
        Assert.Equal("RAP067", diagnostic.Id);
        Assert.Contains("must not be read directly", diagnostic.GetMessage());
    }

    [Fact]
    public async Task AuthenticationEndpointPatternReportsInjectionAndReads()
    {
        var source = Contracts + """
            namespace Example
            {
                public sealed class AuthenticationEndpoint(IConfiguration configuration)
                {
                    public string Configuration()
                    {
                        var settings = configuration.GetSection("Authentication");
                        return settings["Authority"] + settings["PublicClientId"];
                    }
                }
            }
            """;
        Assert.Equal(3, (await AnalyzeAsync(source)).Length);
    }

    [Fact]
    public async Task BindingAndRegisteringSingletonSettingsIsAllowed()
    {
        var source = Contracts + """
            namespace Example
            {
                public sealed class Consumer(Settings settings) { }
                public static class Registration
                {
                    public static void Register(IConfiguration configuration, Services services)
                    {
                        var settings = configuration.GetRequiredSection("Authentication").Get<Settings>();
                        services.AddSingleton(settings);
                        var other = new Settings();
                        configuration.GetSection("Other").Bind(other);
                        services.AddSingleton(other);
                        services.AddSingleton((Settings)configuration.GetSection("Third").Get(typeof(Settings)));
                        services.Configure<Settings>(configuration.GetSection("Authentication"));
                        services.AddSingleton(provider => provider.GetRequiredService<IOptions<Settings>>().Value);
                    }
                }
                public sealed class Services
                {
                    public void Configure<T>(IConfiguration configuration) { }
                    public void AddSingleton<T>(T value) { }
                    public void AddSingleton<T>(System.Func<Provider, T> factory) { }
                }
                public sealed class Provider { public T GetRequiredService<T>() => default; }
            }
            """;
        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    public async Task ValidatedOptionsBindingAndSingletonPocoInjectionAreAllowed()
    {
        var source = Contracts + """
            namespace Microsoft.Extensions.Options
            {
                public sealed class OptionsBuilder<T> where T : class { }
            }
            namespace Microsoft.Extensions.DependencyInjection
            {
                public interface IServiceCollection { }
                public static class OptionsServiceCollectionExtensions
                {
                    public static OptionsBuilder<T> AddOptions<T>(this IServiceCollection services) where T : class => default;
                    public static OptionsBuilder<T> BindConfiguration<T>(this OptionsBuilder<T> builder, string section) where T : class => builder;
                    public static OptionsBuilder<T> ValidateDataAnnotations<T>(this OptionsBuilder<T> builder) where T : class => builder;
                    public static OptionsBuilder<T> ValidateOnStart<T>(this OptionsBuilder<T> builder) where T : class => builder;
                    public static IServiceCollection AddSingleton<T>(this IServiceCollection services,
                        System.Func<System.IServiceProvider, T> factory) where T : class => services;
                    public static T GetRequiredService<T>(this System.IServiceProvider provider) where T : notnull => default;
                }
            }
            namespace Example
            {
                using Microsoft.Extensions.DependencyInjection;

                public sealed class SmtpSettings
                {
                    [System.ComponentModel.DataAnnotations.Required]
                    public string Host { get; set; }
                }
                public sealed class MailSender(SmtpSettings settings) { }
                public sealed class Builder { public IServiceCollection Services { get; set; } }
                public static class Registration
                {
                    public static void Register(Builder builder)
                    {
                        builder.Services.AddOptions<SmtpSettings>()
                            .BindConfiguration("SmtpSettings")
                            .ValidateDataAnnotations()
                            .ValidateOnStart();

                        builder.Services.AddSingleton(provider =>
                            provider.GetRequiredService<IOptions<SmtpSettings>>().Value);
                    }
                }
            }
            """;

        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    public async Task UnrelatedNamesAndConfigurationWritesAreAllowed()
    {
        var source = Contracts + """
            namespace Unrelated
            {
                public interface IConfiguration { string this[string key] { get; } }
                public interface IOptions<T> { }
                public sealed class Consumer(IConfiguration configuration, IOptions<string> options)
                {
                    public string Read() => configuration["Key"];
                }
            }
            namespace Example
            {
                public static class Setup
                {
                    public static void Write(IConfiguration configuration, IConfigurationSection section)
                    {
                        configuration["Key"] = "value";
                        section.Value = "value";
                    }
                }
            }
            """;
        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    public async Task GeneratedCodeIsIgnored()
    {
        var source = "// <auto-generated/>\n" + Contracts + """
            namespace Example
            {
                public class Consumer(IConfiguration configuration)
                {
                    public string Read() => configuration["Key"];
                }
            }
            """;
        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    public async Task TopLevelConfigurationReadsAndCompoundAssignmentsAreErrors()
    {
        var source = Contracts.Replace("using Microsoft.Extensions.Options;", """
            using Microsoft.Extensions.Options;
            IConfiguration configuration = null;
            var value = configuration.GetValue<string>("Key");
            configuration["Key"] += "suffix";
            """);
        Assert.Equal(2, (await AnalyzeAsync(source, OutputKind.ConsoleApplication)).Length);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string source, OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("ConfigurationTests",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12))], references,
            new CSharpCompilationOptions(outputKind));
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        return await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new Rap067Analyzer()))
            .GetAnalyzerDiagnosticsAsync();
    }
}
