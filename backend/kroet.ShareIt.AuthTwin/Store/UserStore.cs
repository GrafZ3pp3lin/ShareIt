using System.Collections.Concurrent;
using kroet.ShareIt.AuthTwin.Realm;

namespace kroet.ShareIt.AuthTwin.Store;

internal sealed record TwinUser(
    string Id,
    string Username,
    string? Email,
    bool EmailVerified,
    bool Enabled,
    string? FirstName,
    string? LastName,
    bool HasPassword);

/// <summary>Users by id. Usernames and emails are stored lowercased, as Keycloak does.</summary>
internal sealed class UserStore
{
    private readonly ConcurrentDictionary<string, TwinUser> _users = new();

    public UserStore(RealmModel realm)
    {
        foreach (var user in realm.Users)
        {
            // Keycloak user ids are UUIDs and the API stores the subject as a Guid.
            var id = Guid.NewGuid().ToString();
            _users[id] = new TwinUser(
                id,
                user.Username,
                user.Email,
                user.EmailVerified,
                user.Enabled,
                user.FirstName,
                user.LastName,
                user.HasPassword);
        }
    }

    public TwinUser? Find(string id) => _users.GetValueOrDefault(id);

    /// <summary>The login form accepts username or email, case-insensitively.</summary>
    public TwinUser? FindByLogin(string login)
    {
        var normalized = login.Trim().ToLowerInvariant();
        return People.FirstOrDefault(u => u.Username == normalized || u.Email == normalized);
    }

    public IEnumerable<TwinUser> People => _users.Values.OrderBy(u => u.Username);
}
