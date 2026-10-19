using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrdenesOnline.Application.Services;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;
using OrdenesOnline.Infrastructure.Persistence;
using OrdenesOnline.Infrastructure.Repositories;
using OrdenesOnline.Tests.TestDoubles;
using OrdenesOnline_API.Features.Propuestas;

namespace OrdenesOnline.Tests;

public sealed class PropuestaIngresadaTests
{
    [Theory]
    [InlineData(MarcarPropuestaIngresadaStatus.RepresentanteNotFound, 401)]
    [InlineData(MarcarPropuestaIngresadaStatus.NotFound, 404)]
    [InlineData(MarcarPropuestaIngresadaStatus.NotSigned, 409)]
    public async Task ControllerReturnsDomainFailure(MarcarPropuestaIngresadaStatus status, int expected)
    {
        var result = await Controller(status).MarkEntered(12, default);
        Assert.Equal(expected, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task ControllerRequiresAnAuthenticatedRepresentative()
    {
        var controller = Controller(MarcarPropuestaIngresadaStatus.Success);
        controller.HttpContext.User = new ClaimsPrincipal();
        Assert.Equal(401, Assert.IsType<ObjectResult>((await controller.MarkEntered(12, default)).Result).StatusCode);
    }

    [Fact]
    public async Task ControllerReturnsEnteredState()
    {
        var response = Assert.IsType<OkObjectResult>((await Controller(MarcarPropuestaIngresadaStatus.Success).MarkEntered(12, default)).Result);
        Assert.Equal(new PropuestaIngresadaResponse(12, "INGRESADO"), response.Value);
    }

    private static PropuestaClienteController Controller(MarcarPropuestaIngresadaStatus status) =>
        new(null!, new PropuestaSeguimientoService(new StubRepository(status)))
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, "7")], "Test"))
            }}
        };

    private sealed class StubRepository(MarcarPropuestaIngresadaStatus status) : IPropuestaSeguimientoRepository
    {
        public Task<PropuestaSeguimientoSnapshot> GetAsync(int id, CancellationToken ct = default) =>
            Task.FromResult(new PropuestaSeguimientoSnapshot(true, []));
        public Task<MarcarPropuestaIngresadaStatus> MarkEnteredAsync(int id, int proposal, CancellationToken ct = default) =>
            Task.FromResult(status);
    }

    [LocalDbTheory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task SqlEnforcesSignaturesScopeAndPreservesEnteredStateOnWebhookRetry(int holderCount)
    {
        var database = "OrdenesOnline_TrackingTest_" + Guid.NewGuid().ToString("N");
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
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(settings.ConnectionString).Options;
            await using var context = new AppDbContext(options);
            await context.Database.ExecuteSqlRawAsync(await DatabaseTestScripts.ReadCreate());
            var scope = new FakeRepresentanteClientScopeRepository();
            var tracking = new PropuestaSeguimientoRepository(context, scope);
            var signing = new PropuestaDocuSignRepository(context);
            var account = Guid.NewGuid();
            var proposals = Enumerable.Range(1, 8).Select(i => new Propuesta
            {
                NombreOperador = "Prueba", CorreoCorporativo = "prueba@example.com", Cosabcli = i == 8 ? "OTHER" : "C001",
                Tipo = "Compra", TipoOrden = "Limite", Cantidad = 10, Instrumento = "ABC", Precio = 5,
                Vigencia = "Hoy", Mercado = "BVL", Estado = i == 2 ? "Pendiente" : i == 3 ? "Cancelado" : "Aceptado"
            }).ToArray();
            proposals[0].Estado = PropuestaEstados.Pendiente;
            proposals[4].Estado = PropuestaEstados.Pendiente;
            context.Propuestas.AddRange(proposals); await context.SaveChangesAsync();
            var signed = await Register(proposals[0], holderCount);
            var pending = await Register(proposals[1], 1);
            var partial = await Register(proposals[4], holderCount);
            await signing.ApplyAsync(partial, new("sent", DateTime.UtcNow, null,
                partial.Firmantes.Select((s, i) => s with { Estado = i < holderCount - 1 ? "completed" : "sent" }).ToArray()), default);
            var obsolete = await Register(proposals[5], 1);
            await Complete(obsolete);
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.PropuestaDocuSign SET EsVigente=0 WHERE PropuestaId={obsolete.PropuestaId}");
            var malformed = await Register(proposals[6], 2);
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.PropuestaDocuSign SET EstadoSobre='completed' WHERE PropuestaId={malformed.PropuestaId}");
            await Complete(signed);
            await signing.ConfirmSentAsync(signed.PropuestaId, signed.EnvelopeId, default);
            Assert.Equal("completed", (await signing.FindAsync(signed.EnvelopeId, account, default))!.Estado);
            await signing.ConfirmSentAsync(pending.PropuestaId, pending.EnvelopeId, default);
            Assert.Equal("sent", (await signing.FindAsync(pending.EnvelopeId, account, default))!.Estado);
            await signing.ApplyAsync(pending, new("created", DateTime.UtcNow, null, pending.Firmantes), default);
            Assert.Equal("sent", (await signing.FindAsync(pending.EnvelopeId, account, default))!.Estado);

            var page = (await new PropuestaSeguimientoService(tracking).Get(7, 1, 20)).Page!;
            Assert.Equal(7, page.TotalCount);
            Assert.Equal("FIRMADO", page.Items.Single(p => p.CodigoOrden == signed.PropuestaId).Estado);
            Assert.True(page.Items.Single(p => p.CodigoOrden == signed.PropuestaId).PuedeMarcarIngresado);
            Assert.Equal("ENVIADO", page.Items.Single(p => p.CodigoOrden == pending.PropuestaId).Estado);
            Assert.False(page.Items.Single(p => p.CodigoOrden == malformed.PropuestaId).PuedeMarcarIngresado);
            Assert.Equal("ENVIADO", page.Items.Single(p => p.CodigoOrden == partial.PropuestaId).Estado);
            Assert.False(page.Items.Single(p => p.CodigoOrden == partial.PropuestaId).PuedeMarcarIngresado);

            // Rechazo real del cliente: finaliza la propuesta y no admite ingreso ni eventos posteriores.
            var rejection = new DocuSignEstado("declined", DateTime.UtcNow, null,
                pending.Firmantes.Select(f => f with { Estado = "declined" }).ToArray());
            await signing.ApplyAsync(pending, rejection, default);
            await signing.ApplyAsync(pending, rejection, default); // webhook repetido
            await Complete(pending); // una notificacion posterior no reabre la propuesta
            var cancelled = (await new PropuestaSeguimientoService(tracking).Get(7, 1, 20)).Page!
                .Items.Single(p => p.CodigoOrden == pending.PropuestaId);
            Assert.Equal("CANCELADO", cancelled.Estado);
            Assert.False(cancelled.PuedeMarcarIngresado);
            Assert.Equal(MarcarPropuestaIngresadaStatus.NotSigned, await tracking.MarkEnteredAsync(7, pending.PropuestaId));
            Assert.Equal("Cancelado", (await context.Propuestas.AsNoTracking()
                .SingleAsync(p => p.PropuestaId == pending.PropuestaId)).Estado);
            Assert.DoesNotContain(await signing.PendingDownloadsAsync(account, default), p => p.PropuestaId == pending.PropuestaId);
            foreach (var p in proposals.Skip(1).Take(6))
                Assert.Equal(MarcarPropuestaIngresadaStatus.NotSigned, await tracking.MarkEnteredAsync(7, p.PropuestaId));
            Assert.Equal(MarcarPropuestaIngresadaStatus.NotFound, await tracking.MarkEnteredAsync(7, proposals[7].PropuestaId));
            Assert.Equal(MarcarPropuestaIngresadaStatus.NotFound, await tracking.MarkEnteredAsync(7, int.MaxValue));
            Assert.Equal(MarcarPropuestaIngresadaStatus.RepresentanteNotFound,
                await new PropuestaSeguimientoRepository(context, new FakeRepresentanteClientScopeRepository(false)).MarkEnteredAsync(7, signed.PropuestaId));

            await using var otherContext = new AppDbContext(options);
            var otherTracking = new PropuestaSeguimientoRepository(otherContext, scope);
            var results = await Task.WhenAll(tracking.MarkEnteredAsync(7, signed.PropuestaId), otherTracking.MarkEnteredAsync(7, signed.PropuestaId));
            Assert.All(results, r => Assert.Equal(MarcarPropuestaIngresadaStatus.Success, r));
            await Complete(signed); // webhook duplicado no revierte Ingresado a Aceptado
            var entered = (await new PropuestaSeguimientoService(tracking).Get(7, 1, 20)).Page!.Items.Single(p => p.CodigoOrden == signed.PropuestaId);
            Assert.Equal("INGRESADO", entered.Estado); Assert.False(entered.PuedeMarcarIngresado);

            async Task<DocuSignSobre> Register(Propuesta p, int count)
            {
                var envelope = Guid.NewGuid();
                await signing.RegisterAsync(p.PropuestaId, envelope, account, Guid.NewGuid(),
                    Enumerable.Range(1, count).Select(i => new DocuSignFirmante(i.ToString(), count == 1 ? "Cliente" : $"Titular{i}",
                        "Prueba", "+51999999999", "created")).ToArray(), default);
                return (await signing.FindAsync(envelope, account, default))!;
            }
            Task Complete(DocuSignSobre s) => signing.ApplyAsync(s, new("completed", DateTime.UtcNow, DateTime.UtcNow,
                s.Firmantes.Select(f => f with { Estado = "completed", FechaFirmaUtc = DateTime.UtcNow }).ToArray()), default);
        }
        finally
        {
            await Execute(master, $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];");
        }
    }
    private static async Task Execute(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
