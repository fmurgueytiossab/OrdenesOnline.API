using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OrdenesOnline.Application.Services;
using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;
using OrdenesOnline.Tests.TestDoubles;

namespace OrdenesOnline.Tests;

public sealed class PropuestaClienteServiceTests
{
    [Fact]
    public async Task Create_SavesProposalBeforeSendingSummaryEmail()
    {
        var propuestaRepository = new FakePropuestaRepository();
        var emailService = new FakeEmailService(() => Assert.Equal(1, propuestaRepository.AddCalls));
        var service = CreateService(propuestaRepository, emailService);

        var result = await service.Create(7, CreateRequest("canaccord renta4"));

        Assert.Equal(CreatePropuestaClienteStatus.Created, result.Status);
        Assert.Equal(123, result.PropuestaId);
        Assert.True(result.EmailDelivered);
        Assert.Equal("cliente@example.com", emailService.To);
        Assert.Contains("Canaccord Renta4", emailService.Body);
        Assert.Contains("Compra", emailService.Body);
        Assert.Contains("ABC", emailService.Body);
        Assert.Contains(
            "http://localhost:4200/Clientes/propuestas/revision?token=",
            emailService.Body);
        Assert.Equal("Canaccord Renta4", propuestaRepository.SavedProposal?.Mercado);
        Assert.Equal("Pendiente", propuestaRepository.SavedProposal?.Estado);
    }

    [Fact]
    public async Task Create_WhenEmailFails_KeepsProposalAndReportsPendingEmail()
    {
        var propuestaRepository = new FakePropuestaRepository();
        var emailService = new FakeEmailService(throwOnSend: true);
        var service = CreateService(propuestaRepository, emailService);

        var result = await service.Create(7, CreateRequest("Pershing"));

        Assert.Equal(CreatePropuestaClienteStatus.Created, result.Status);
        Assert.Equal(123, result.PropuestaId);
        Assert.False(result.EmailDelivered);
        Assert.Equal(1, propuestaRepository.AddCalls);
    }

    [Fact]
    public async Task Create_WhenCosabcliIsNotAuthorized_DoesNotSaveOrSendEmail()
    {
        var propuestaRepository = new FakePropuestaRepository();
        var emailService = new FakeEmailService();
        var service = CreateService(propuestaRepository, emailService);
        var request = CreateRequest("BVL");
        request.Cosabcli = "NO-AUTORIZADO";

        var result = await service.Create(7, request);

        Assert.Equal(CreatePropuestaClienteStatus.CosabcliForbidden, result.Status);
        Assert.Equal(0, propuestaRepository.AddCalls);
        Assert.Equal(0, emailService.SendCalls);
    }

    [Fact]
    public async Task Create_WhenClientBelongsToResolvedManager_SavesProposal()
    {
        var propuestaRepository = new FakePropuestaRepository();
        var emailService = new FakeEmailService();
        var clientScopeRepository = new FakeRepresentanteClientScopeRepository(
            clientCodes: ["C001", "C002"]);
        var service = CreateService(
            propuestaRepository,
            emailService,
            clientScopeRepository: clientScopeRepository);
        var request = CreateRequest("BVL");
        request.Cosabcli = "C002";

        var result = await service.Create(7, request);

        Assert.Equal(CreatePropuestaClienteStatus.Created, result.Status);
        Assert.Equal("C002", propuestaRepository.SavedProposal?.Cosabcli);
    }

    [Fact]
    public async Task Create_WhenMarketIsNotSupported_DoesNotSaveOrSendEmail()
    {
        var propuestaRepository = new FakePropuestaRepository();
        var emailService = new FakeEmailService();
        var service = CreateService(propuestaRepository, emailService);

        var result = await service.Create(7, CreateRequest("Extranjero"));

        Assert.Equal(CreatePropuestaClienteStatus.InvalidMarket, result.Status);
        Assert.Equal(0, propuestaRepository.AddCalls);
        Assert.Equal(0, emailService.SendCalls);
    }

    [Fact]
    public async Task Decide_WithReviewToken_AcceptsProposalAndConsumesToken()
    {
        var propuestaRepository = new FakePropuestaRepository();
        await propuestaRepository.AddAsync(new Propuesta
        {
            NombreOperador = "Representante",
            CorreoCorporativo = "representante@example.com",
            Cosabcli = "C001",
            Tipo = "Compra",
            TipoOrden = "Mercado",
            Cantidad = 10,
            Instrumento = "ABC",
            Mercado = "BVL",
            Vigencia = "Día"
        });
        var tokenRepository = new FakeActionTokenRepository();
        var service = CreateService(
            propuestaRepository,
            new FakeEmailService(),
            tokenRepository);
        var actionTokenService = CreateActionTokenService(tokenRepository);
        var issued = await actionTokenService.CreateProposalReviewTokenAsync(7, 123);

        var review = await service.GetReview(issued.Value);
        var decision = await service.Decide(issued.Value, "aceptado");

        Assert.Equal(PropuestaClienteReviewStatus.Valid, review.Status);
        Assert.Equal(PropuestaClienteDecisionStatus.Updated, decision.Status);
        Assert.Equal("Aceptado", decision.Estado);
        Assert.Null(await actionTokenService.ValidateAsync(
            issued.Value,
            ActionTokenService.ProposalReviewType));
    }

    private static PropuestaClienteService CreateService(
        IPropuestaRepository propuestaRepository,
        IEmailService emailService,
        FakeActionTokenRepository? tokenRepository = null,
        IRepresentanteClientScopeRepository? clientScopeRepository = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:ClientesFrontendUrl"] = "http://localhost:4200",
                ["ActionTokens:ProposalReviewMinutes"] = "1440"
            })
            .Build();
        var actionTokenService = new ActionTokenService(
            tokenRepository ?? new FakeActionTokenRepository(),
            configuration);

        return new PropuestaClienteService(
            propuestaRepository,
            new FakeRepresentanteRepository(),
            clientScopeRepository ?? new FakeRepresentanteClientScopeRepository(),
            emailService,
            actionTokenService,
            NullLogger<PropuestaClienteService>.Instance,
            configuration);
    }

    private static ActionTokenService CreateActionTokenService(
        FakeActionTokenRepository repository)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ActionTokens:ProposalReviewMinutes"] = "1440"
            })
            .Build();

        return new ActionTokenService(repository, configuration);
    }

    private static PropuestaClienteCreateRequest CreateRequest(string market) => new()
    {
        CorreoCliente = "cliente@example.com",
        Cosabcli = "C001",
        Tipo = "Compra",
        TipoOrden = "Mercado",
        Cantidad = 10,
        Instrumento = "ABC",
        Mercado = market,
        Moneda = "USD",
        Vigencia = "Día"
    };

    private sealed class FakeEmailService : IEmailService
    {
        private readonly Action? _beforeSend;
        private readonly bool _throwOnSend;

        public FakeEmailService(Action? beforeSend = null, bool throwOnSend = false)
        {
            _beforeSend = beforeSend;
            _throwOnSend = throwOnSend;
        }

        public FakeEmailService(bool throwOnSend) : this(null, throwOnSend)
        {
        }

        public int SendCalls { get; private set; }
        public string? To { get; private set; }
        public string? Body { get; private set; }

        public Task SendEmailAsync(
            string to,
            string subject,
            string body,
            CancellationToken cancellationToken = default)
        {
            _beforeSend?.Invoke();
            SendCalls++;
            To = to;
            Body = body;

            return _throwOnSend
                ? Task.FromException(new InvalidOperationException("SMTP no disponible"))
                : Task.CompletedTask;
        }
    }

    private sealed class FakePropuestaRepository : IPropuestaRepository
    {
        public int AddCalls { get; private set; }
        public Propuesta? SavedProposal { get; private set; }

        public Task AddAsync(Propuesta propuesta, CancellationToken cancellationToken = default)
        {
            AddCalls++;
            propuesta.PropuestaId = 123;
            SavedProposal = propuesta;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(int id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IEnumerable<Propuesta>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<Propuesta>>([]);

        public Task<Propuesta?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(SavedProposal?.PropuestaId == id ? SavedProposal : null);

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
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> UpdatePassword(
            string correoCorporativo,
            string password,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task UpdateAsync(Representante representante, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

}
