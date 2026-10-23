using System.Text.Json;
using kroet.ShareIt.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;


namespace kroet.ShareIt.Api.Test.Services;

[TestClass]
public sealed class GlobalExceptionHandlerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task TryHandleAsync_Writes500ProblemDetailsAndHandlesException()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var context = new DefaultHttpContext();
        using var requestServices = new ServiceCollection()
            .AddLogging()
            .AddProblemDetails()
            .BuildServiceProvider();
        context.RequestServices = requestServices;
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/v1/content/package";
        var body = new MemoryStream();
        context.Response.Body = body;

        var handled = await handler.TryHandleAsync(context, new InvalidOperationException("boom"), CancellationToken.None);

        Assert.IsTrue(handled);
        Assert.AreEqual(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.AreEqual("application/problem+json", context.Response.ContentType);

        body.Position = 0;
        using var document = await JsonDocument.ParseAsync(body);
        var root = document.RootElement;
        Assert.AreEqual(500, root.GetProperty("status").GetInt32());
        Assert.AreEqual("Internal server error", root.GetProperty("title").GetString());
        Assert.AreEqual("An unexpected error occurred while processing the request.", root.GetProperty("detail").GetString());
    }
}
