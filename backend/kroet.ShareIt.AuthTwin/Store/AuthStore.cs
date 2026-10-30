using System.Collections.Concurrent;
using System.Security.Cryptography;
using kroet.ShareIt.AuthTwin.Realm;
using Microsoft.AspNetCore.WebUtilities;

namespace kroet.ShareIt.AuthTwin.Store;

internal sealed record AuthRequest(
    string ClientId,
    string RedirectUri,
    string? State,
    string? Nonce,
    IReadOnlyList<string> Scopes,
    string? CodeChallenge,
    string? DpopJkt,
    DateTimeOffset ExpiresAt);

internal sealed record AuthCode(AuthRequest Request, string UserId, string SessionId, DateTimeOffset ExpiresAt);

/// <summary>
/// An SSO session. Idle time counts from the last refresh, max time from <paramref name="Started"/>;
/// <paramref name="AuthTime"/> is when the user signed in.
/// </summary>
internal sealed record UserSession(string Id, string UserId, DateTimeOffset AuthTime, DateTimeOffset Started, DateTimeOffset LastRefresh);

/// <summary>Logins in progress, codes, sessions and used DPoP proof ids. Everything lives only as long as the process.</summary>
internal sealed class AuthStore(RealmModel realm, TimeProvider time)
{
    // Keycloak's defaults: login form valid 30 minutes, code valid 60 seconds.
    public static readonly TimeSpan LoginLifespan = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan CodeLifespan = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, AuthRequest> _logins = new();
    private readonly ConcurrentDictionary<string, AuthCode> _codes = new();
    private readonly ConcurrentDictionary<string, UserSession> _sessions = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _usedProofs = new();
    private long _nextPurge;

    public string StartLogin(AuthRequest request)
    {
        PurgeExpired();
        var id = NewId();
        _logins[id] = request;
        return id;
    }

    public AuthRequest? FindLogin(string id) =>
        _logins.TryGetValue(id, out var request) && request.ExpiresAt > time.GetUtcNow() ? request : null;

    public void EndLogin(string id) => _logins.TryRemove(id, out _);

    public UserSession StartSession(string userId)
    {
        var now = time.GetUtcNow();
        var session = new UserSession(NewId(), userId, now, now, now);
        _sessions[session.Id] = session;
        return session;
    }

    /// <summary>The SSO session if it is still within its idle and max lifetime; refreshing it resets the idle time.</summary>
    public UserSession? RefreshSession(string sessionId, bool touch = true) =>
        Refresh(_sessions, sessionId, realm.SsoSessionIdleTimeout, realm.SsoSessionMaxLifespan, touch);

    public bool EndSession(string sessionId) => _sessions.TryRemove(sessionId, out _);

    /// <summary>Ends a user's SSO sessions; returns how many ended.</summary>
    public int EndUserSessions(string userId)
    {
        var ended = 0;
        foreach (var (key, session) in _sessions)
            if (session.UserId == userId && _sessions.TryRemove(key, out _)) ended++;
        return ended;
    }

    public string IssueCode(AuthRequest request, UserSession session)
    {
        PurgeExpired();
        var code = NewId(32);
        _codes[code] = new AuthCode(request, session.UserId, session.Id, time.GetUtcNow() + CodeLifespan);
        return code;
    }

    /// <summary>A code works once; whoever takes it uses it up, whether the exchange succeeds or not.</summary>
    public AuthCode? TakeCode(string code) =>
        _codes.TryRemove(code, out var authCode) && authCode.ExpiresAt > time.GetUtcNow() ? authCode : null;

    /// <summary>Records a DPoP proof's jti; false if it was seen before.</summary>
    public bool TryUseProof(string jti, DateTimeOffset forgetAfter)
    {
        PurgeExpired();
        return _usedProofs.TryAdd(jti, forgetAfter);
    }

    /// <param name="touch">False only checks that the session is alive, without resetting its idle time.</param>
    private UserSession? Refresh<TKey>(
        ConcurrentDictionary<TKey, UserSession> sessions,
        TKey key,
        TimeSpan idle,
        TimeSpan max,
        bool touch)
        where TKey : notnull
    {
        var now = time.GetUtcNow();
        while (sessions.TryGetValue(key, out var session))
        {
            if (now > session.LastRefresh + idle || now > session.Started + max)
            {
                sessions.TryRemove(key, out _);
                return null;
            }

            if (!touch) return session;
            var refreshed = session with { LastRefresh = now };
            if (sessions.TryUpdate(key, refreshed, session)) return refreshed;
        }

        return null;
    }

    /// <summary>At most once a minute, so a busy test run doesn't walk every entry on every request.</summary>
    private void PurgeExpired()
    {
        var now = time.GetUtcNow();
        var next = Interlocked.Read(ref _nextPurge);
        if (now.UtcTicks < next || Interlocked.CompareExchange(ref _nextPurge, (now + TimeSpan.FromMinutes(1)).UtcTicks, next) != next) return;

        foreach (var (key, login) in _logins)
            if (login.ExpiresAt <= now) _logins.TryRemove(key, out _);
        foreach (var (key, code) in _codes)
            if (code.ExpiresAt <= now) _codes.TryRemove(key, out _);
        foreach (var (key, expiry) in _usedProofs)
            if (expiry < now) _usedProofs.TryRemove(key, out _);
        foreach (var (key, session) in _sessions)
            if (now > session.LastRefresh + realm.SsoSessionIdleTimeout || now > session.Started + realm.SsoSessionMaxLifespan)
                _sessions.TryRemove(key, out _);
    }

    private static string NewId(int bytes = 18) => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(bytes));
}
