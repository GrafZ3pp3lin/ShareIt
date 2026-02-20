using FluentValidation;

namespace kroet.ShareIt.Api.Content.Configuration;

/// <summary>
/// Configuration options for PostgreSQL content package storage.
/// </summary>
public sealed class PostgreSqlContentStorageSettings : ISettings
{
    /// <summary>
    /// PostgreSQL connection string.
    /// </summary>
    public required string ConnectionString { get; init; }

    /// <summary>
    /// Whether to automatically initialize the database schema on startup.
    /// </summary>
    public bool AutoInitializeSchema { get; init; } = true;

    public static string SectionName => "ContentStorage:PostgreSqlDb";
}

public sealed class PostgreSqlDbSettingsValidator : AbstractValidator<PostgreSqlContentStorageSettings>
{
    public PostgreSqlDbSettingsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
    }
}

