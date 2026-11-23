namespace kroet.ShareIt.Api.Services.Health;

/// <summary>
/// Probe mode for the chiseled runtime image, which ships no shell or HTTP
/// client: <c>dotnet kroet.ShareIt.Api.dll --healthcheck [url]</c> performs a
/// single GET against the readiness endpoint and returns the exit code, so
/// Docker healthchecks can probe the API from inside the container without
/// extra tools.
/// </summary>
public static class HealthCheckProbe
{
    /// <summary>
    /// Runs one probe against the readiness endpoint.
    /// </summary>
    /// <param name="url">
    /// The URL to probe, or null for the image default
    /// <c>http://127.0.0.1:8080/health/ready</c> (the port pinned in compose).
    /// </param>
    /// <returns>0 when the endpoint answers 2xx, 1 otherwise.</returns>
    public static async Task<int> RunAsync(string? url)
    {
        url = string.IsNullOrWhiteSpace(url)
            ? "http://127.0.0.1:8080/health/ready"
            : url;

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception)
        {
            // Connection refused, timeout, DNS failure: the API is not ready.
            return 1;
        }
    }
}
