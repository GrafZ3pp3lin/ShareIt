using FluentValidation;
using kroet.ShareIt.Api.Configuration;

namespace kroet.ShareIt.Api.Storage.Postgres;

/// <summary>
/// Configuration options for PostgreSQL content package storage.
/// </summary>
/// <remarks>
/// Properties use setters (not init-only setters): the source-generated
/// configuration binder does not populate init-only properties.
/// </remarks>
public sealed class SqlStorageSettings : ISettings
{
    /// <summary>
    /// PostgreSQL connection string.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Whether to automatically initialize the database schema on startup.
    /// </summary>
    public bool AutoInitializeSchema { get; set; } = true;

    public static string SectionName => "ContentStorage:PostgreSqlDb";
}

public sealed class PostgreSqlDbSettingsValidator : AbstractValidator<SqlStorageSettings>
{
    public PostgreSqlDbSettingsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
    }
}

