using System.Data;
using System.Data.Common;

// 使用不具备网络能力的模拟驱动，任何连接尝试都会立即中断。
internal static class FakeDatabase
{
    public static int ProviderConstructed;
}

internal sealed class FakeConnectionReachedException : Exception { }

internal abstract class FakeConnection : DbConnection
{
    protected FakeConnection() { FakeDatabase.ProviderConstructed++; throw new FakeConnectionReachedException(); }
    public override string ConnectionString { get => string.Empty; set { } }
    public override string Database => string.Empty;
    public override string DataSource => string.Empty;
    public override string ServerVersion => string.Empty;
    public override ConnectionState State => ConnectionState.Closed;
    public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
    public override void Close() => throw new NotSupportedException();
    public override void Open() => throw new NotSupportedException();
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
    protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
}

namespace MySql.Data.MySqlClient
{
    internal sealed class MySqlConnection : FakeConnection
    {
        public MySqlConnection(string? connectionString) { }
    }
    internal sealed class MySqlException : DbException { public int Number => 0; }
}

namespace Dm
{
    internal sealed class DmConnection : FakeConnection
    {
        public DmConnection(string? connectionString) { }
    }
    internal sealed class DmException : DbException { public int Number => 0; }
}

namespace Dapper
{
    internal readonly struct CommandDefinition
    {
        public CommandDefinition(string sql, object? parameters = null, DbTransaction? transaction = null,
            CancellationToken cancellationToken = default) { }
    }

    internal static class SqlMapper
    {
        public static Task<IEnumerable<T>> QueryAsync<T>(this DbConnection connection, CommandDefinition command) => throw new NotSupportedException();
        public static Task<T?> QuerySingleOrDefaultAsync<T>(this DbConnection connection, CommandDefinition command) => throw new NotSupportedException();
        public static Task<T> ExecuteScalarAsync<T>(this DbConnection connection, CommandDefinition command) => throw new NotSupportedException();
        public static Task<int> ExecuteAsync(this DbConnection connection, CommandDefinition command) => throw new NotSupportedException();
        public static List<T> AsList<T>(this IEnumerable<T> items) => items.ToList();
    }
}
