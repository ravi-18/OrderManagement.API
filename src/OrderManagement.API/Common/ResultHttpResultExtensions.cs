using Microsoft.AspNetCore.Http.HttpResults;

namespace OrderManagement.API.Common;

public static class ResultHttpResultExtensions
{
    public static ProblemHttpResult ToProblem(this Error error) =>
        TypedResults.Problem(ProblemDetailsMapper.FromError(error));

    public static ProblemHttpResult ToProblem(this Result result)
    {
        if (result.IsSuccess || result.Error is null)
        {
            throw new InvalidOperationException("Cannot convert a successful Result to a problem response.");
        }

        return result.Error.ToProblem();
    }

    public static ProblemHttpResult ToProblem<T>(this Result<T> result)
    {
        if (result.IsSuccess || result.Error is null)
        {
            throw new InvalidOperationException("Cannot convert a successful Result to a problem response.");
        }

        return result.Error.ToProblem();
    }
}
