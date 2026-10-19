using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrdenesOnline.Domain.interfaces;
using OrdenesOnline.Infrastructure.Persistence;
using OrdenesOnline.Infrastructure.Repositories;
using OrdenesOnline.Tests.TestDoubles;

namespace OrdenesOnline.Tests;

public sealed class DocuSignThreeSchemaTests
{
    [LocalDbFact]
    public async Task CreateSupportsTwoAndThreeHolderEnvelopesWithoutAllowingFour()
    {
        var database = "OrdenesOnline_ThreeSignersTest_" + Guid.NewGuid().ToString("N");
        var settings = new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\MSSQLLocalDB", InitialCatalog = "master",
            IntegratedSecurity = true, Encrypt = false, Pooling = false, ConnectTimeout = 15
        };
        await using var master = new SqlConnection(settings.ConnectionString);
        await master.OpenAsync();
        await Execute(master, $"CREATE DATABASE [{database}];");
        try
        {
            settings.InitialCatalog = database;
            await using var connection = new SqlConnection(settings.ConnectionString);
            await connection.OpenAsync();
            var initial = await DatabaseTestScripts.ReadCreate();
            await Execute(connection, initial);
            foreach (var id in new[] { 1, 2, 3 }) await DatabaseTestScripts.InsertProposal(connection, id);
            await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options);
            var repository = new PropuestaDocuSignRepository(context);
            var account = Guid.NewGuid(); var oldEnvelope = Guid.NewGuid(); var newEnvelope = Guid.NewGuid();
            await repository.RegisterAsync(1, oldEnvelope, account, Guid.NewGuid(), Signers(2), default);
            var previous = (await repository.FindAsync(oldEnvelope, account, default))!;
            Assert.Equal(2, previous.FirmasRequeridas); Assert.Equal(2, previous.Firmantes.Count);
            Assert.Equal("created", previous.Estado);
            await repository.RegisterAsync(2, newEnvelope, account, Guid.NewGuid(), Signers(3), default);
            Assert.Equal(3, (await repository.FindAsync(newEnvelope, account, default))!.Firmantes.Count);
            await Assert.ThrowsAsync<ArgumentException>(() => repository.RegisterAsync(3, Guid.NewGuid(), account, Guid.NewGuid(), Signers(4), default));
            await Assert.ThrowsAsync<SqlException>(() => Execute(connection, "UPDATE dbo.PropuestaDocuSign SET FirmasRequeridas=4 WHERE PropuestaId=2;"));
            await Assert.ThrowsAsync<SqlException>(() => Execute(connection, "UPDATE dbo.PropuestaDocuSignFirmante SET Funcion='Titular4' WHERE Funcion='Titular3';"));
        }
        finally
        {
            await Execute(master, $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];");
        }
    }

    private static DocuSignFirmante[] Signers(int count) => Enumerable.Range(1, count)
        .Select(i => new DocuSignFirmante(i.ToString(), $"Titular{i}", "Cuenta prueba", $"+519{i:00000000}", "created")).ToArray();
    private static async Task Execute(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
