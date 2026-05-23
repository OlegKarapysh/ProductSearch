using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace FullTextSearchPostgres.Benchmarks.Diagnosers;

// Captures the SQL text and parameter values of the next EF-Core-issued command.
// Used by BenchmarkInspector to re-issue the same query under EXPLAIN ANALYZE.
internal sealed class CaptureCommandInterceptor : DbCommandInterceptor
{
    public string? LastSql { get; private set; }
    public List<NpgsqlParameter> LastParameters { get; } = new();

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Capture(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Capture(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void Capture(DbCommand command)
    {
        LastSql = command.CommandText;
        LastParameters.Clear();
        foreach (DbParameter p in command.Parameters)
        {
            if (p is NpgsqlParameter np)
            {
                LastParameters.Add(new NpgsqlParameter
                {
                    ParameterName = np.ParameterName,
                    NpgsqlDbType  = np.NpgsqlDbType,
                    Value         = np.Value,
                });
            }
        }
    }
}
