using OrderManagement.API.Common;

namespace OrderManagement.API.Tests.Common;

public sealed class ResultTests
{
    [Fact]
    public void Success_Typed_HasValueAndNoError()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_Typed_HasErrorAndNoValue()
    {
        var error = Error.Conflict("Insufficient stock");
        var result = Result.Failure<int>(error);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
        Assert.Equal("conflict", result.Error!.Code);
    }

    [Fact]
    public void Failure_Typed_ValueAccessThrows()
    {
        var result = Result.Failure<string>(Error.NotFound("Order not found"));

        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void Success_Untyped_IsSuccessWithoutError()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_Untyped_CarriesError()
    {
        var error = Error.Validation("Invalid request");
        var result = Result.Failure(error);

        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
    }
}
