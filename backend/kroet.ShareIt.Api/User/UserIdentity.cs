namespace kroet.ShareIt.Api.User;

public sealed record UserIdentity
{
    public required Guid Id { get; init; }
    public required bool IsGuest { get; init; }
}