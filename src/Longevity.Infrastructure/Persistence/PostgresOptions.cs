namespace Longevity.Infrastructure.Persistence;

public sealed class PostgresOptions
{
    public const string SectionName = "Postgres";

    public bool Enabled { get; init; }

    public string? ConnectionString { get; init; }

    public void EnsureValid()
    {
        if (!Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new ArgumentException("A PostgreSQL connection string is required when Postgres persistence is enabled.", nameof(ConnectionString));
        }
    }
}
