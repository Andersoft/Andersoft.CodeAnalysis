using Andersoft.CodeAnalysis;
using Xunit;

namespace Andersoft.CodeAnalysis.Tests;

public sealed class MessageHandlerAnalyzerTests
{
    private const string Contracts = """
        namespace Andersoft.Messaging.Abstractions
        {
            public interface IMessageHandler<in TMessage> { }
            public interface IMessageHandler<in TMessage, TResult> { }
        }
        namespace OneOf
        {
            public readonly struct OneOf<TSuccess, TError> { }
            public readonly struct OneOf<T0, T1, T2> { }
        }
        """;

    [Fact]
    public async Task ValueReturningMessageHandlerWithoutOneOfIsFlagged()
    {
        var source = Contracts + """
            namespace Example;
            sealed class GetOrder { }
            sealed class Order { }
            sealed class GetOrderHandler : Andersoft.Messaging.Abstractions.IMessageHandler<GetOrder, Order> { }
            """;

        var diagnostic = Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap061Analyzer()));

        Assert.Equal("RAP061", diagnostic.Id);
        Assert.Contains("GetOrderHandler", diagnostic.GetMessage());
        Assert.Contains("Order", diagnostic.GetMessage());
    }

    [Fact]
    public async Task ValueReturningMessageHandlerWithSuccessErrorOneOfIsAllowed()
    {
        var source = Contracts + """
            namespace Example;
            sealed class GetOrder { }
            sealed class Order { }
            sealed class OrderError { }
            sealed class GetOrderHandler : Andersoft.Messaging.Abstractions.IMessageHandler<GetOrder, OneOf.OneOf<Order, OrderError>> { }
            """;

        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap061Analyzer()));
    }

    [Fact]
    public async Task MessageHandlerWithoutReturnValueIsAllowed()
    {
        var source = Contracts + """
            namespace Example;
            sealed class OrderCreated { }
            sealed class OrderCreatedHandler : Andersoft.Messaging.Abstractions.IMessageHandler<OrderCreated> { }
            """;

        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap061Analyzer()));
    }

    [Fact]
    public async Task OneOfWithMoreThanSuccessAndErrorIsFlagged()
    {
        var source = Contracts + """
            namespace Example;
            sealed class GetOrder { }
            sealed class Order { }
            sealed class NotFound { }
            sealed class Forbidden { }
            sealed class GetOrderHandler : Andersoft.Messaging.Abstractions.IMessageHandler<GetOrder, OneOf.OneOf<Order, NotFound, Forbidden>> { }
            """;

        Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap061Analyzer()));
    }

    [Fact]
    public async Task UnrelatedMessageHandlerInterfaceIsIgnored()
    {
        var source = Contracts + """
            namespace Example;
            interface IMessageHandler<TMessage, TResult> { }
            sealed class GetOrder { }
            sealed class Order { }
            sealed class GetOrderHandler : IMessageHandler<GetOrder, Order> { }
            """;

        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(source, new Rap061Analyzer()));
    }
}
