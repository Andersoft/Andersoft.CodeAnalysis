using Microsoft.CodeAnalysis;
using Xunit;

namespace Andersoft.CodeAnalysis.Tests;

public sealed class MongoRepositoryAnalyzerTests
{
    private const string Contracts = """
        namespace Andersoft.Domain
        {
            public interface IRepositoryBase<T, TId> { }
        }
        namespace MongoDB.Driver
        {
            public interface IMongoCollection<T>
            {
                void InsertOne(T value);
                System.Threading.Tasks.Task DeleteOneAsync(T value);
            }
            public interface IMongoDatabase
            {
                IMongoCollection<T> GetCollection<T>(string name);
            }
            public abstract class Collection<T> : IMongoCollection<T>
            {
                public abstract void InsertOne(T value);
                public abstract System.Threading.Tasks.Task DeleteOneAsync(T value);
            }
            public static class CollectionExtensions
            {
                public static T Find<T>(this IMongoCollection<T> collection) => default(T);
                public static void UpdateOne<T>(this IMongoCollection<T> collection, T value) { }
            }
        }
        """;

    [Theory]
    [InlineData("collection.InsertOne(1);")]
    [InlineData("_ = collection.DeleteOneAsync(1);")]
    [InlineData("_ = collection.Find();")]
    [InlineData("collection.UpdateOne(1);")]
    [InlineData("CollectionExtensions.UpdateOne(collection, 1);")]
    [InlineData("collection?.InsertOne(1);")]
    [InlineData("System.Action<int> insert = collection.InsertOne;")]
    [InlineData("System.Func<int> find = collection.Find;")]
    [InlineData("System.Action insert = () => collection.InsertOne(1);")]
    [InlineData("void Insert() { collection.InsertOne(1); }")]
    public async Task DirectCollectionOperationsAreErrors(string statement)
    {
        var source = Contracts + $$"""
            namespace Example
            {
                using MongoDB.Driver;
                public class Consumer
                {
                    public void Run(IMongoCollection<int> collection) { {{statement}} }
                }
            }
            """;

        var diagnostic = Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap065Analyzer()));

        Assert.Equal("RAP065", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Andersoft.Domain.IRepositoryBase", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("public class Consumer", "Collection<int>")]
    [InlineData("public class Consumer<T> where T : IMongoCollection<int>", "T")]
    [InlineData("public class Consumer", "AliasedCollection")]
    public async Task ConcreteCollectionsConstraintsAndAliasesAreFlagged(string declaration, string collectionType)
    {
        var source = Contracts + $$"""
            namespace Example
            {
                using MongoDB.Driver;
                using AliasedCollection = MongoDB.Driver.IMongoCollection<int>;
                {{declaration}}
                {
                    public void Run({{collectionType}} collection) { collection.InsertOne(1); }
                }
            }
            """;

        Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap065Analyzer()));
    }

    [Theory]
    [InlineData("Andersoft.Domain.IRepositoryBase<int, int>")]
    [InlineData("BaseRepository")]
    [InlineData("ISpecialRepository")]
    public async Task RepositoryImplementationsAndExtensionsAreAllowed(string baseType)
    {
        var source = Contracts + $$"""
            namespace Example
            {
                using MongoDB.Driver;
                public class BaseRepository : Andersoft.Domain.IRepositoryBase<int, int> { }
                public interface ISpecialRepository : Andersoft.Domain.IRepositoryBase<int, int> { }
                public class Repository : {{baseType}}
                {
                    public void Run(IMongoCollection<int> collection)
                    {
                        collection.InsertOne(1);
                        _ = collection.Find();
                        CollectionExtensions.UpdateOne(collection, 1);
                        System.Action<int> insert = collection.InsertOne;
                        System.Action delayed = () => collection.InsertOne(1);
                    }
                }
            }
            """;

        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap065Analyzer()));
    }

    [Fact]
    public async Task RepositoryNamesAndUnrelatedInterfacesDoNotGrantAnExemption()
    {
        var source = Contracts + """
            namespace Example
            {
                public interface IRepositoryBase<T, TId> { }
                public class RepositoryBase : IRepositoryBase<int, int>
                {
                    public void Run(MongoDB.Driver.IMongoCollection<int> collection) { collection.InsertOne(1); }
                }
            }
            """;

        Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap065Analyzer()));
    }

    [Fact]
    public async Task InjectingARepositoryDoesNotAllowDirectCollectionAccess()
    {
        var source = Contracts + """
            namespace Example
            {
                public class Consumer
                {
                    public void Run(Andersoft.Domain.IRepositoryBase<int, int> repository,
                        MongoDB.Driver.IMongoCollection<int> collection) { collection.InsertOne(1); }
                }
            }
            """;

        Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap065Analyzer()));
    }

    [Fact]
    public async Task GeneratedCodeIsIgnored()
    {
        var source = Contracts + """
            namespace Example
            {
                [System.CodeDom.Compiler.GeneratedCode("Generator", "1.0")]
                public class Consumer
                {
                    public void Run(MongoDB.Driver.IMongoCollection<int> collection) { collection.InsertOne(1); }
                }
            }
            """;

        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap065Analyzer()));
    }

    [Fact]
    public async Task CollectionAcquisitionAndUnrelatedOperationsAreAllowed()
    {
        var source = Contracts + """
            namespace Example
            {
                public interface IMongoCollection<T> { void InsertOne(T value); }
                public class Consumer
                {
                    public void Run(IMongoCollection<int> unrelated, MongoDB.Driver.IMongoDatabase database)
                    {
                        unrelated.InsertOne(1);
                        var collection = database.GetCollection<int>("items");
                        _ = collection.ToString();
                    }
                }
            }
            """;

        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap065Analyzer()));
    }
}
