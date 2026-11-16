using kroet.ShareIt.Api.Configuration;
using kroet.ShareIt.Api.Models;
using Microsoft.Extensions.Options;

namespace kroet.ShareIt.Api.Storage.InMemory;

public class InMemoryDownloadTokenService : IDownloadTokenService
{
    private readonly Dictionary<Guid, DownloadToken> _tokens = new();
    private readonly DownloadSettings _downloadSettings;
    private readonly TimeProvider _timeProvider;

    public InMemoryDownloadTokenService(IOptions<DownloadSettings> downloadSettings, TimeProvider timeProvider)
    {
        _downloadSettings = downloadSettings.Value;
        _timeProvider = timeProvider;
    }

    public Task<DownloadToken> GenerateTokenAsync(Guid contentId, Guid clientId, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var token = new DownloadToken
        {
            Token = Guid.NewGuid().ToString(),
            ContentId = contentId,
            ClientId = clientId,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_downloadSettings.DefaultTokenExpirationMinutes),
            Id = Guid.NewGuid()
        };

        _tokens.Add(token.Id, token);

        return Task.FromResult(token);
    }

    public Task<TokenValidationResult> ValidateTokenAsync(string token, Guid clientId, CancellationToken cancellationToken = default)
    {
        var tokenEntry = _tokens.Values.FirstOrDefault(t => t.Token == token);
        if (tokenEntry is null)
        {
            return Task.FromResult(new TokenValidationResult(false, null, "Token not found"));
        }

        if (tokenEntry.ClientId != clientId)
        {
            return Task.FromResult(new TokenValidationResult(false, null, "Token does not belong to the client"));
        }

        if (tokenEntry.ExpiresAt < _timeProvider.GetUtcNow())
        {
            return Task.FromResult(new TokenValidationResult(false, null, "Token has expired"));
        }

        return Task.FromResult(new TokenValidationResult(true, tokenEntry.ContentId));
    }

    public async Task<TokenValidationResult> ConsumeTokenAsync(string token, Guid clientId, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateTokenAsync(token, clientId, cancellationToken);
        if (!validation.IsValid)
        {
            return validation;
        }

        var tokenEntry = _tokens.Values.First(t => t.Token == token);
        _tokens.Remove(tokenEntry.Id);

        return new TokenValidationResult(true, tokenEntry.ContentId);
    }

    public Task<long> CleanupExpiredTokensAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var expiredTokens = _tokens.Values.Where(t => t.ExpiresAt < now).ToList();
        foreach (var token in expiredTokens)
        {
            _tokens.Remove(token.Id);
        }

        return Task.FromResult((long)expiredTokens.Count);
    }
}