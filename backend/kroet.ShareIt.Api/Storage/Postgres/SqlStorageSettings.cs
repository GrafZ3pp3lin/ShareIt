using FluentValidation;
using kroet.ShareIt.Api.Configuration;

namespace kroet.ShareIt.Api.Storage.Postgres;

/// <summary>
/// Configuration options for PostgreSQL content package storage.
/// </summary>
public sealed class SqlStorageSettings : ISettings
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

public sealed class PostgreSqlDbSettingsValidator : AbstractValidator<SqlStorageSettings>
{
    public PostgreSqlDbSettingsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
    }
}

