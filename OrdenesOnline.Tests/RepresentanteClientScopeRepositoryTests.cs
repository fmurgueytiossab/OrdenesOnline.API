using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrdenesOnline.Infrastructure.Persistence;
using OrdenesOnline.Infrastructure.Repositories;

namespace OrdenesOnline.Tests;

public sealed class RepresentanteClientScopeRepositoryTests
{
    [Theory]
    [InlineData("026775", "GESTOR01", true)]
    [InlineData("026775", null, false)]
    [InlineData("999999", "GESTOR01", false)]
    public async Task ResolvesGestorFromRepresentativeClientRowBeforeFindingClients(
        string representativeCode, string? representativeGestor, bool seesLorena)
    {
        // SQL simulado: Fernando cosabcli=026775; Lorena cosabcli=012345, gestor=GESTOR01.
        // No debe confundir el cosabcli del representante con el gestor de sus clientes.
        // Usar el provider SQL Server ejercita las consultas reales sin conectar a una base.
        using var appConnection = new QueryConnection(command =>
            command.CommandText.Contains("CodeRepresentante") ? Rows(typeof(string), representativeCode) : Rows(typeof(bool), true));
        using var opersabConnection = new QueryConnection(command =>
        {
            var parameters = command.Parameters.Cast<DbParameter>().Select(p => p.Value?.ToString() ?? "").ToArray();
            var operands = command.CommandText + string.Join(" ", parameters);
            if (command.CommandText.Contains("SELECT DISTINCT [c].[gestor]", StringComparison.OrdinalIgnoreCase))
                return Rows(typeof(string), operands.Contains("026775") && representativeGestor is not null
                    ? [representativeGestor] : []);
            return Rows(typeof(string), operands.Contains("GESTOR01") ? ["012345"] : []);
        });
        await using var app = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(appConnection).Options);
        await using var opersab = new OpersabDbContext(new DbContextOptionsBuilder<OpersabDbContext>().UseSqlServer(opersabConnection).Options);
        var result = await new RepresentanteClientScopeRepository(app, opersab).GetAsync(7);
        Assert.True(result.RepresentanteExiste);
        Assert.Equal(seesLorena ? new[] { "GESTOR01" } : [], result.Gestores);
        Assert.Equal(seesLorena ? new[] { "012345" } : [], result.Cosabcli);
    }

    private static DbDataReader Rows(Type type, params object[] values)
    {
        var table = new DataTable(); table.Columns.Add("Value", type);
        foreach (var value in values) table.Rows.Add(value);
        return table.CreateDataReader();
    }

    private sealed class QueryConnection(Func<DbCommand, DbDataReader> execute) : DbConnection
    {
        private ConnectionState _state;
        [AllowNull] public override string ConnectionString { get; set; } = "Server=localhost;Database=Test;Integrated Security=true;TrustServerCertificate=true";
        public override string Database => "Test";
        public override string DataSource => "localhost";
        public override string ServerVersion => "16.0";
        public override ConnectionState State => _state;
        public override void Open() => _state = ConnectionState.Open;
        public override void Close() => _state = ConnectionState.Closed;
        public override void ChangeDatabase(string name) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new QueryCommand(this, execute);
    }

    private sealed class QueryCommand(DbConnection connection, Func<DbCommand, DbDataReader> execute) : DbCommand
    {
        private readonly SqlCommand _parameters = new();
        [AllowNull] public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = connection;
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => _parameters.Parameters;
        public override void Cancel() { }
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        public override object ExecuteScalar() => throw new NotSupportedException();
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new SqlParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => execute(this);
        protected override void Dispose(bool disposing) { if (disposing) _parameters.Dispose(); base.Dispose(disposing); }
    }
}
