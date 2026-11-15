namespace kroet.ShareIt.Api.User;

/// <summary>
/// The caller behind a request: either an authenticated user or a guest that
/// supplied a client-generated id. The closed hierarchy fixes the two cases at
/// compile time, so switches over an identity are exhaustive.
/// </summary>
public closed record UserIdentity
{
    public required Guid Id { get; init; }

    public bool IsGuest => this is GuestUserIdentity;
}

public sealed record AuthenticatedUserIdentity : UserIdentity;

public sealed record GuestUserIdentity : UserIdentity;
