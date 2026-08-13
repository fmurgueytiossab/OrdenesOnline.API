using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OrdenesOnline.Application.Services;
using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;
using System.Net;

namespace OrdenesOnline.Tests;

public sealed class PropuestaServiceTests
{
    [Fact]
    public async Task Create_WhenZapierFails_KeepsProposalAndReportsPendingNotification()
    {
        var propuestaRepository = new FakePropuestaRepository();
        var representanteRepository = new FakeRepresentanteRepository();
        var service = CreateService(
            propuestaRepository,
            representanteRepository,
            HttpStatusCode.InternalServerError);

        var result = await service.Create(7, CreateRequest());

        Assert.Equal(CreatePropuestaStatus.Created, result.Status);
        Assert.Equal(123, result.PropuestaId);
        Assert.False(result.NotificationDelivered);
        Assert.Equal(1, propuestaRepository.AddCalls);
    }

    [Fact]
    public async Task Create_WhenCosabcliIsNotAuthorized_DoesNotSaveProposal()
    {
        var propuestaRepository = new FakePropuestaRepository();
        var representanteRepository = new FakeRepresentanteRepository();
        var service = CreateService(
            propuestaRepository,
            representanteRepository,
            HttpStatusCode.OK);
        var request = CreateRequest();
        request.Cosabcli = "NO-AUTORIZADO";

        var result = await service.Create(7, request);

        Assert.Equal(CreatePropuestaStatus.CosabcliForbidden, result.Status);
        Assert.Equal(0, propuestaRepository.AddCalls);
    }

    private static PropuestaService CreateService(
        IPropuestaRepository propuestaRepository,
        IRepresentanteRepository representanteRepository,
        HttpStatusCode zapierStatus)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:ZapierWebhookUrl"] = "https://example.test/webhook"
            })
            .Build();
        var httpClient = new HttpClient(new StubHttpMessageHandler(zapierStatus));
        var zapierService = new ZapierService(httpClient, configuration);

        return new PropuestaService(
            propuestaRepository,
            representanteRepository,
            zapierService,
            NullLogger<PropuestaService>.Instance);
    }

    private static PropuestaCreateRequest CreateRequest() => new()
    {
        Cosabcli = "C001",
        Tipo = "Compra",
        TipoOrden = "Mercado",
        Cantidad = 10,
        Instrumento = "ABC",
        Mercado = "Local",
        Moneda = "PEN",
        Vigencia = "Día"
    };

    private sealed class FakePropuestaRepository : IPropuestaRepository
    {
        public int AddCalls { get; private set; }

        public Task AddAsync(Propuesta propuesta, CancellationToken cancellationToken = default)
        {
            AddCalls++;
            propuesta.PropuestaId = 123;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(int id, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IEnumerable<Propuesta>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<Propuesta>>([]);

        public Task<Propuesta?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Propuesta?>(null);

        public Task UpdateAsync(Propuesta propuesta, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeRepresentanteRepository : IRepresentanteRepository
    {
        public Task<RepresentanteDTO?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<RepresentanteDTO?>(new RepresentanteDTO
            {
                RepresentanteId = id,
                Nombre = "Representante",
                CorreoCorporativo = "representante@example.com",
                Dni = "12345678",
                Cosabcli = ["C001"]
            });

        public Task AddAsync(Representante representante, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(int id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IEnumerable<Representante>> GetAllAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Representante?> GetByEmail(string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PasswordValidationResult?> Login(
            string correoCorporativo,
            string password,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> UpdatePassword(
            string correoCorporativo,
            string password,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpdateAsync(Representante representante, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public StubHttpMessageHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_statusCode));
    }
}
