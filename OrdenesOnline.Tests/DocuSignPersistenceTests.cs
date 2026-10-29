using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrdenesOnline.Domain.interfaces;
using OrdenesOnline.Infrastructure.Persistence;
using OrdenesOnline.Infrastructure.Repositories;
using OrdenesOnline.Tests.TestDoubles;

namespace OrdenesOnline.Tests;

public sealed class DocuSignPersistenceTests
{
    // Opt-in: usa exclusivamente LocalDB y una base temporal creada por esta prueba.
    // PowerShell: $env:ORDENESONLINE_RUN_LOCALDB_TESTS='1'; dotnet test ... --no-restore
    [LocalDbFact]
    public async Task EnvelopeTransitionsPersistWithoutDownloadMetadata()
    {
        var database = "OrdenesOnline_DocuSignTest_" + Guid.NewGuid().ToString("N");
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
            await VerifyTransitions(settings.ConnectionString);
        }
        finally
        {
            // Nombre generado arriba; ninguna base existente se elimina.
            await Execute(master, $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];");
        }
    }

    private static async Task VerifyTransitions(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await Execute(connection, await DatabaseTestScripts.ReadCreate());
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options);
        var repository = new PropuestaDocuSignRepository(context);
        var account = Guid.NewGuid();
        var date = new DateTime(2026, 10, 9, 15, 0, 0, DateTimeKind.Utc);
        var proposalId = 0;
        foreach (var terminal in new[] { "declined", "voided" })
        foreach (var count in new[] { 1, 2, 3 })
        {
            var tracked = await Register(++proposalId, count);
            var signers = tracked.Firmantes.Select((s, i) => s with
            {
                Estado = terminal == "declined" && i == (count - 1) ? "declined" : "completed"
            }).ToArray();
            var state = new DocuSignEstado(terminal, date, null, signers);
            await repository.ApplyAsync(tracked, state, default);
            await AssertStored(tracked.Id, terminal, "Cancelado");
            await repository.ApplyAsync(tracked, state, default); // notificacion repetida
            await repository.ApplyAsync(tracked, state with { Estado = "sent", FechaCambioEstadoUtc = date.AddMinutes(-1) }, default);
            await repository.ApplyAsync(tracked, state with // consulta posterior no cambia un estado terminal
            {
                Estado = "completed", FechaCambioEstadoUtc = date.AddMinutes(1),
                Firmantes = signers.Select(s => s with { Estado = "completed" }).ToArray()
            }, default);
            await AssertStored(tracked.Id, terminal, "Cancelado");
        }

        var approved = await Register(++proposalId, 2);
        Assert.Equal(approved.Id, (await repository.FindByProposalAsync(approved.PropuestaId, account, default))?.Id);
        Assert.Null(await repository.FindByProposalAsync(approved.PropuestaId, Guid.NewGuid(), default));
        var partial = approved.Firmantes.Select((s, i) => s with { Estado = i == 0 ? "completed" : "sent" }).ToArray();
        await repository.ApplyAsync(approved, new("sent", date, null, partial), default);
        await AssertStored(approved.Id, "sent", "Pendiente");
        var complete = partial.Select(s => s with { Estado = "completed", FechaFirmaUtc = date.AddMinutes(1) }).ToArray();
        await repository.ApplyAsync(approved, new("completed", date.AddMinutes(1), date.AddMinutes(1), complete), default);
        await AssertStored(approved.Id, "completed", "Aceptado");
        async Task<DocuSignSobre> Register(int id, int count)
        {
            await DatabaseTestScripts.InsertProposal(connection, id);
            var signers = Enumerable.Range(1, count).Select(i => new DocuSignFirmante(i.ToString(),
                count == 1 ? "Cliente" : $"Titular{i}", "Prueba", "+51999999999", "created")).ToArray();
            var envelope = Guid.NewGuid();
            await repository.RegisterAsync(id, envelope, account, Guid.NewGuid(), signers, default);
            return (await repository.FindAsync(envelope, account, default))!;
        }

        async Task AssertStored(long id, string envelope, string proposal)
        {
            await using var command = new SqlCommand($"""
                SELECT d.EstadoSobre,p.Estado,d.FechaCambioEstadoUtc
                FROM dbo.PropuestaDocuSign d JOIN dbo.propuesta p ON p.PropuestaId=d.PropuestaId
                WHERE d.PropuestaDocuSignId={id};
                """, connection);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(envelope, reader.GetString(0));
            Assert.Equal(proposal, reader.GetString(1)); Assert.False(reader.IsDBNull(2));
        }
    }

    private static async Task Execute(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 30 };
        await command.ExecuteNonQueryAsync();
    }
}

internal sealed class LocalDbTheoryAttribute : TheoryAttribute
{
    public LocalDbTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("ORDENESONLINE_RUN_LOCALDB_TESTS") != "1")
            Skip = "Requiere ORDENESONLINE_RUN_LOCALDB_TESTS=1 y SQL Server LocalDB para la base temporal.";
    }
}

internal sealed class LocalDbFactAttribute : FactAttribute
{
    public LocalDbFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ORDENESONLINE_RUN_LOCALDB_TESTS") != "1")
            Skip = "Requiere ORDENESONLINE_RUN_LOCALDB_TESTS=1 y SQL Server LocalDB para la base temporal.";
    }
}
