using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.API.Common;
using DomainValidationException = OrderManagement.API.Common.ValidationException;

namespace OrderManagement.API.Tests.Common;

public sealed class ProblemDetailsMapperTests
{
    [Fact]
    public void FromException_NotFound_MapsTo404()
    {
        var problem = ProblemDetailsMapper.FromException(new NotFoundException("Order not found"));

        AssertMapped(problem, StatusCodes.Status404NotFound, "Not Found", "Order not found", "not_found");
    }

    [Fact]
    public void FromException_Conflict_MapsTo409()
    {
        var problem = ProblemDetailsMapper.FromException(new ConflictException("Insufficient stock"));

        AssertMapped(problem, StatusCodes.Status409Conflict, "Conflict", "Insufficient stock", "conflict");
    }

    [Fact]
    public void FromException_DomainValidation_MapsTo400()
    {
        var problem = ProblemDetailsMapper.FromException(new DomainValidationException("CustomerId is required"));

        AssertMapped(problem, StatusCodes.Status400BadRequest, "Bad Request", "CustomerId is required", "validation");
    }

    [Fact]
    public void FromException_BusinessRule_MapsTo422()
    {
        var problem = ProblemDetailsMapper.FromException(new BusinessRuleException("Invalid status transition"));

        AssertMapped(
            problem,
            StatusCodes.Status422UnprocessableEntity,
            "Unprocessable Entity",
            "Invalid status transition",
            "business_rule");
    }

    [Fact]
    public void FromException_Unhandled_MapsTo500WithoutSecretsOrStacks()
    {
        var secret = "password=super-secret;Host=db.internal";
        var problem = ProblemDetailsMapper.FromException(new InvalidOperationException(secret));

        Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
        Assert.Equal("Internal Server Error", problem.Title);
        Assert.Equal("An unexpected error occurred.", problem.Detail);
        Assert.Equal("internal_error", problem.Extensions["code"]);
        Assert.Contains("500", problem.Type, StringComparison.Ordinal);
        Assert.DoesNotContain("password", problem.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("super-secret", problem.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", problem.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secret, problem.Title, StringComparison.OrdinalIgnoreCase);
        Assert.False(problem.Extensions.ContainsKey("exception"));
        Assert.False(problem.Extensions.ContainsKey("stackTrace"));
    }

    [Theory]
    [InlineData("not_found", StatusCodes.Status404NotFound, "Not Found")]
    [InlineData("conflict", StatusCodes.Status409Conflict, "Conflict")]
    [InlineData("validation", StatusCodes.Status400BadRequest, "Bad Request")]
    [InlineData("business_rule", StatusCodes.Status422UnprocessableEntity, "Unprocessable Entity")]
    public void FromError_MapsKnownCodes(string code, int status, string title)
    {
        var error = new Error(code, "Headline", "More detail");

        var problem = ProblemDetailsMapper.FromError(error);

        AssertMapped(problem, status, title, "More detail", code);
    }

    [Fact]
    public void FromError_UsesMessageWhenDetailMissing()
    {
        var problem = ProblemDetailsMapper.FromError(Error.NotFound("Order not found"));

        Assert.Equal("Order not found", problem.Detail);
    }

    [Fact]
    public void FromError_UnknownCode_MapsTo500WithoutLeakingDetail()
    {
        var problem = ProblemDetailsMapper.FromError(new Error("weird", "secret token abc"));

        Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
        Assert.Equal("An unexpected error occurred.", problem.Detail);
        Assert.DoesNotContain("secret", problem.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("internal_error", problem.Extensions["code"]);
    }

    [Fact]
    public void ToProblem_Error_ReturnsProblemHttpResult()
    {
        var result = Error.Conflict("Insufficient stock").ToProblem();

        var problemResult = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>(result);
        Assert.Equal((int)HttpStatusCode.Conflict, problemResult.StatusCode);
        Assert.Equal("Insufficient stock", problemResult.ProblemDetails.Detail);
        Assert.Equal("conflict", problemResult.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public void ToProblem_FailedResult_ReturnsProblemHttpResult()
    {
        var result = Result.Failure(Error.Validation("Invalid request")).ToProblem();

        var problemResult = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problemResult.StatusCode);
    }

    private static void AssertMapped(ProblemDetails problem, int status, string title, string detail, string code)
    {
        Assert.Equal(status, problem.Status);
        Assert.Equal(title, problem.Title);
        Assert.Equal(detail, problem.Detail);
        Assert.Equal(code, problem.Extensions["code"]);
        Assert.False(string.IsNullOrWhiteSpace(problem.Type));
        Assert.Contains(status.ToString(), problem.Type, StringComparison.Ordinal);
    }
}
