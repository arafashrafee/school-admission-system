using System.Runtime.CompilerServices;
using Xunit;

// Both stress suites share a finite PostgreSQL instance with the running app.
// Serialize test classes; each stress test still submits 100 parallel requests.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

internal static class TestDatabase
{
    [ModuleInitializer]
    internal static void ConfigureConnectionPool()
    {
        // Set one pool key before Program or either test class reads configuration.
        // The explicit pool also prevents the existing service tests adding a second key.
        const string key = "ConnectionStrings__DefaultConnection";
        var connection = Environment.GetEnvironmentVariable(key)
            ?? "Host=postgres;Port=5432;Database=school_admission;Username=postgres;Password=postgres";
        if (!connection.Contains("Maximum Pool Size", StringComparison.OrdinalIgnoreCase))
            connection = $"{connection.TrimEnd(';')};Maximum Pool Size=100;Connection Idle Lifetime=10;";
        Environment.SetEnvironmentVariable(key, connection);
    }
}
