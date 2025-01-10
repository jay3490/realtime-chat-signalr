namespace Chat.Infrastructure.Persistence;

public enum DatabaseProvider
{
    /// <summary>PostgreSQL via Npgsql. Schema is managed by EF Core migrations.</summary>
    Postgres,

    /// <summary>
    /// SQLite, for running locally without a database server. The schema is created
    /// directly from the model because the migrations target PostgreSQL.
    /// </summary>
    Sqlite,
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Postgres;

    /// <summary>Apply pending migrations (or create the SQLite schema) when the API starts.</summary>
    public bool MigrateOnStartup { get; set; }
}
