using Microsoft.AspNetCore.Diagnostics;

namespace kroet.ShareIt.Api.Services;

/// <summary>
/// Maps unhandled endpoint exceptions to a generic ProblemDetails response
/// (HTTP 500) so storage/database failures surface as RFC 7807 problems
/// instead of bare server responses leaking internals.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // Client-caused malformed requests (e.g. unreadable form bodies) are not
        // server faults: let them propagate so the server answers with 4xx.
        if (exception is BadHttpRequestException)
        {
            return false;
        }

        logger.LogError(
            exception,
            "Unhandled exception while processing {Method} {Path}",
            httpContext.Request.Method,
            httpContext.Request.Path);

        var problem = Results.Problem(
            title: "Internal server error",
            detail: "An unexpected error occurred while processing the request.",
            statusCode: StatusCodes.Status500InternalServerError);

        await problem.ExecuteAsync(httpContext);
        return true;
    }
}
