using System.Diagnostics;

namespace kroet.ShareIt.Api.Services.Observability;

/// <summary>
/// Writes one compact, structured log event per request instead of ASP.NET
/// Core's multi-line request diagnostics ("Request starting", "Executing
/// endpoint", ...). The exported OTLP record also carries the request's trace
/// and span ids.
/// </summary>
internal sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            LogCompletion(context, start, ex);
            throw;
        }

        LogCompletion(context, start, exception: null);
    }

    private void LogCompletion(HttpContext context, long start, Exception? exception)
    {
        var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        // Match the previous request-logging semantics: server errors are logged
        // as errors, everything else (including 4xx) as information.
        var level = exception is not null || context.Response.StatusCode >= StatusCodes.Status500InternalServerError
            ? LogLevel.Error
            : LogLevel.Information;

        logger.Log(
            level,
            exception,
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms",
            context.Request.Method,
            context.Request.Path.Value,
            context.Response.StatusCode,
            elapsedMs);
    }
}

internal static class RequestLoggingExtensions
{
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app)
        => app.UseMiddleware<RequestLoggingMiddleware>();
}
