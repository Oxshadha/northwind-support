namespace SupportDesk.IntegrationTests;

/// <summary>
/// A test that needs a real SQL Server, for SQL that SQLite cannot run (DATEDIFF). Skipped
/// unless SUPPORTDESK_SQLSERVER holds a connection string to a server it may create databases
/// on, e.g. the docker compose one:
/// SUPPORTDESK_SQLSERVER="Server=localhost,1433;User Id=sa;Password=Local_Dev_Password1;TrustServerCertificate=True;Encrypt=False"
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public const string Variable = "SUPPORTDESK_SQLSERVER";

    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = $"Set {Variable} to run tests against SQL Server.";
        }
    }
}
