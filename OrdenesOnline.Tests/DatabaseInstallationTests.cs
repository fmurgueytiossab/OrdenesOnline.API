using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using OrdenesOnline.Application.Services;
using Microsoft.EntityFrameworkCore;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Infrastructure.Persistence;
using OrdenesOnline.Infrastructure.Repositories;
using OrdenesOnline.Tests.TestDoubles;

namespace OrdenesOnline.Tests;

public sealed class DatabaseInstallationTests
{
    [LocalDbFact]
    public async Task ConsolidatedCreateSeedAndRepositoriesWorkOnAnEmptyDatabase()
    {
        var database = "OrdenesOnline_InstallTest_" + Guid.NewGuid().ToString("N");
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
            var create = await DatabaseTestScripts.ReadCreate();
            await Execute(connection, create);
            Assert.Equal(6, await Scalar(connection, "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0;"));
            Assert.Equal(1, await Scalar(connection, """
                SELECT COUNT(*) FROM sys.columns
                WHERE object_id=OBJECT_ID(N'dbo.PropuestaDocuSign') AND name=N'FechaCambioEstadoUtc';
                """));
            Assert.Equal(0, await Scalar(connection, """
                SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.PropuestaDocuSign')
                    AND name IN (N'FechaEstadoUtc',N'EstadoDescarga',N'UbicacionArchivo',N'Sha256Archivo',
                        N'FechaDescargaUtc',N'IntentosDescarga',N'UltimoError');
                """));
            Assert.Equal(5, await Scalar(connection, "SELECT COUNT(*) FROM sys.procedures WHERE is_ms_shipped=0;"));
            Assert.Equal(0, await Scalar(connection, "SELECT COUNT(*) FROM sys.symmetric_keys WHERE name='DATAWEB_KEY';"));
            Assert.Equal(2, await Scalar(connection, """
                SELECT COUNT(*) FROM sys.columns c JOIN sys.types t ON c.user_type_id=t.user_type_id
                WHERE c.object_id=OBJECT_ID(N'dbo.Token') AND c.is_nullable=0
                  AND ((c.name='TokenHash' AND t.name='char' AND c.max_length=64)
                    OR (c.name='Type' AND t.name='varchar' AND c.max_length=20));
                """));
            var seed = await DatabaseTestScripts.ReadSeed();
            await Execute(connection, seed);
            await Execute(connection, seed);
            Assert.Equal(2, await Scalar(connection, "SELECT COUNT(*) FROM dbo.UserRepresentante;"));
            Assert.Equal(2, await Scalar(connection, "SELECT COUNT(*) FROM dbo.CodeRepresentante;"));
            Assert.Equal(2, await Scalar(connection, "SELECT COUNT(*) FROM dbo.CodeRepresentante WHERE (RepresentanteId=1 AND Cosabcli='026775') OR (RepresentanteId=33 AND Cosabcli='006186');"));
            Assert.Equal(0, await Scalar(connection, "SELECT COUNT(*) FROM dbo.CodeRepresentante c LEFT JOIN dbo.UserRepresentante u ON u.RepresentanteId=c.RepresentanteId WHERE u.RepresentanteId IS NULL;"));
            await Assert.ThrowsAsync<SqlException>(() => Execute(connection, create));
            Assert.Equal(2, await Scalar(connection, "SELECT COUNT(*) FROM dbo.UserRepresentante;"));

            // Claves efimeras solo en esta base temporal. No pretenden descifrar el dataset original.
            await Execute(connection, $"CREATE MASTER KEY ENCRYPTION BY PASSWORD='{Guid.NewGuid():N}';");
            await Execute(connection, "CREATE CERTIFICATE DATAWEB_CERTIFICATE WITH SUBJECT='Prueba local de instalacion'; CREATE SYMMETRIC KEY DATAWEB_KEY WITH ALGORITHM=AES_256 ENCRYPTION BY CERTIFICATE DATAWEB_CERTIFICATE;");
            await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options);
            var representatives = new RepresentanteRepository(context);
            var representative = (await representatives.GetByIdAsync(1))!;
            Assert.Single(representative.Cosabcli);
            var newPassword = "Test-" + Guid.NewGuid().ToString("N")[..8];
            Assert.True(await representatives.UpdatePassword(representative.CorreoCorporativo, newPassword));
            var valid = await representatives.Login(representative.CorreoCorporativo, newPassword);
            Assert.Equal(1, valid!.IsValid); Assert.Equal(1, valid.RepresentanteId);
            Assert.Equal(0, (await representatives.Login(representative.CorreoCorporativo, "incorrecta"))!.IsValid);
            // Repetir el seed no restaura la contrasena cifrada inicial sobre un usuario existente.
            await Execute(connection, seed);
            Assert.Equal(1, (await representatives.Login(representative.CorreoCorporativo, newPassword))!.IsValid);
            await using (var getPassword = new SqlCommand("EXEC dbo.usp_getRepresentantePassword @correo;", connection))
            {
                getPassword.Parameters.AddWithValue("@correo", representative.CorreoCorporativo);
                Assert.True(Equals(newPassword, await getPassword.ExecuteScalarAsync()));
            }

            await DatabaseTestScripts.InsertProposal(connection, 1);
            var tokenRepository = new ActionTokenRepository(context);
            var now = DateTime.UtcNow;
            var tokenService = new ActionTokenService(tokenRepository, new ConfigurationBuilder().Build());
            var issued = await tokenService.CreateProposalReviewTokenAsync(1, 1);
            var token = (await tokenService.ValidateAsync(issued.Value, ActionTokenTypes.ProposalReview))!;
            Assert.Equal(64, token.TokenHash.Length);
            Assert.NotNull(await tokenRepository.GetActiveByHashAsync(token.TokenHash, token.Type, now));
            Assert.True(await tokenRepository.TryApplyProposalDecisionAsync(token.TokenId, 1, PropuestaEstados.Aceptado, now));
            Assert.Null(await tokenRepository.GetActiveByHashAsync(token.TokenHash, token.Type, now));
            Assert.Equal(PropuestaEstados.Aceptado, (await context.Propuestas.AsNoTracking().SingleAsync()).Estado);
            var reset = await tokenService.CreatePasswordResetTokenAsync(1);
            Assert.NotNull(await tokenService.ValidateAsync(reset.Value, ActionTokenTypes.PasswordReset));
            Assert.True(await tokenService.TryMarkUsedAsync(reset.TokenId));
            var invalidType = await Assert.ThrowsAsync<SqlException>(() => Execute(connection, """
                INSERT dbo.Token (UserId,TokenHash,Type,CreatedAt,ExpiresAt,IsUsed,IsRevoked)
                VALUES (1,REPLICATE('B',64),'otro',SYSUTCDATETIME(),DATEADD(MINUTE,10,SYSUTCDATETIME()),0,0);
                """));
            Assert.Contains("CK_Token_Type", invalidType.Message);

            // Una colision de correo/ID se rechaza sin modificar el dataset ya instalado.
            await Execute(connection, "UPDATE dbo.UserRepresentante SET Correo_Corporativo='conflicto@example.com' WHERE RepresentanteId=33;");
            await Assert.ThrowsAsync<SqlException>(() => Execute(connection, seed));
            Assert.Equal(2, await Scalar(connection, "SELECT COUNT(*) FROM dbo.UserRepresentante;"));
        }
        finally
        {
            await Execute(master, $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];");
        }
    }

    private static async Task Execute(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 30 };
        await command.ExecuteNonQueryAsync();
    }
    private static async Task<int> Scalar(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
