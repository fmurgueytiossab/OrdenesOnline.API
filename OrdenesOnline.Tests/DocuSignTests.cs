using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OrdenesOnline.Application.Services;
using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;
using OrdenesOnline.Tests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OrdenesOnline_API.Features.Propuestas;
using OrdenesOnline.Infrastructure.Repositories;

namespace OrdenesOnline.Tests;

public sealed class DocuSignTests
{
    [Theory]
    [InlineData("999999999")]
    [InlineData("51999999999")]
    [InlineData("+51 999 999 999")]
    [InlineData("051999999999")]
    [InlineData(" 051 999 999 999 ")]
    public void IndividualContacts_NormalizesPeruPrefixesAndSpaces(string rawPhone)
    {
        var result = ClienteFirmantesRepository.Resolve(null, "N", rawPhone, [], "Cliente", "Prueba");
        Assert.Null(result.Error);
        Assert.Equal("+51999999999", Assert.Single(result.Firmantes).Celular);
    }

    [Theory]
    [InlineData("05199999999")]
    [InlineData("0519999999999")]
    [InlineData("051899999999")]
    [InlineData("051999999999;988888888")]
    public void IndividualContacts_DoesNotAcceptMalformedPhonesWith051(string rawPhone)
    {
        var result = ClienteFirmantesRepository.Resolve(null, "N", rawPhone, [], "Cliente", "Prueba");
        Assert.NotNull(result.Error);
        Assert.Empty(result.Firmantes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Descripcion que no corresponde al nombre")]
    public void IndividualContacts_UsesPersonalNameWithoutRequiringDescli(string? description)
    {
        var result = ClienteFirmantesRepository.Resolve(description, null, "999999999", [],
            " Lorena Veronica ", " Sanchez ", " Torres ");
        Assert.Null(result.Error);
        var signer = Assert.Single(result.Firmantes);
        Assert.Equal("Lorena Veronica Sanchez Torres", signer.Nombre);
        Assert.Equal("Cliente", signer.Funcion);
        Assert.Equal("+51999999999", signer.Celular);
    }

    [Fact]
    public void IndividualContacts_OmitsMissingSurname()
    {
        var result = ClienteFirmantesRepository.Resolve(null, "N", "999999999", [], "Lorena", "Sanchez", null);
        Assert.Null(result.Error);
        Assert.Equal("Lorena Sanchez", Assert.Single(result.Firmantes).Nombre);
    }

    [Fact]
    public void IndividualContacts_EmptyPersonalNameDoesNotUseAccountDescription()
    {
        var result = ClienteFirmantesRepository.Resolve("Descripcion", "N", "999999999", [], " ", null, "");
        Assert.Empty(result.Firmantes);
        Assert.Contains("nombres, apepat y apemat", result.Error);
    }

    [Fact]
    public void JointContacts_SharedAccountNameRetainsBothPhones()
    {
        var result = ClienteFirmantesRepository.Resolve("Cuenta Juan y Miguel", "S", "999111111", ["+51 999-999-999", "988888888"]);
        Assert.Null(result.Error);
        Assert.Equal(2, result.Firmantes.Count);
        Assert.Equal(new[] { "Titular1", "Titular2" }, result.Firmantes.Select(f => f.Funcion));
        Assert.All(result.Firmantes, f => Assert.Equal("Cuenta Juan y Miguel", f.Nombre));
        Assert.Equal(2, result.Firmantes.Select(f => f.Celular).Distinct().Count());
        Assert.DoesNotContain(result.Firmantes, f => f.Celular == "+51999111111");
    }

    [Theory]
    [InlineData("999999999", "999999999")]
    [InlineData("999999999", "invalido")]
    public void JointContacts_InvalidOrRepeatedPhonesBlockSending(string first, string second)
    {
        var result = ClienteFirmantesRepository.Resolve("Cuenta", "M", "999111111", [first, second]);
        Assert.NotNull(result.Error);
        Assert.Empty(result.Firmantes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Webhook_RequiresValidHmacBeforeProcessing(bool valid)
    {
        using var fixture = new Fixture(2);
        var repository = new FakeDocuSignRepository { Found = fixture.Tracked() };
        var controller = WebhookController(fixture, repository, valid);
        var response = await controller.Webhook(default);
        if (valid)
        {
            Assert.IsType<OkResult>(response);
            Assert.Equal("completed", repository.Applied?.Estado);
            Assert.Equal(2, repository.Applied?.Firmantes.Count);
        }
        else
        {
            Assert.IsType<UnauthorizedResult>(response);
            Assert.Equal(0, repository.ApplyCalls);
        }
    }

    [Fact]
    public async Task Webhook_OneSignatureDoesNotCompleteJointProposal()
    {
        using var fixture = new Fixture(2);
        fixture.Handler.EnvelopeStatus = "sent"; fixture.Handler.SecondStatus = "sent";
        var repository = new FakeDocuSignRepository { Found = fixture.Tracked() };
        Assert.IsType<OkResult>(await WebhookController(fixture, repository, true).Webhook(default));
        Assert.Equal("sent", repository.Applied?.Estado);
    }

    [Fact]
    public async Task Webhook_PersistenceFailureReturns503ForConnectRetry()
    {
        using var fixture = new Fixture(2);
        var repository = new FakeDocuSignRepository { Found = fixture.Tracked(), FailApply = true };
        var result = Assert.IsType<StatusCodeResult>(await WebhookController(fixture, repository, true).Webhook(default));
        Assert.Equal(503, result.StatusCode);
    }

    private static DocuSignController WebhookController(Fixture fixture, FakeDocuSignRepository repository, bool valid)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { @event = "envelope-completed",
            data = new { accountId = fixture.Account, envelopeId = fixture.Envelope } });
        var context = new DefaultHttpContext(); context.Request.Body = new MemoryStream(bytes);
        context.Request.Headers["X-Docusign-Signature-1"] = valid
            ? Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes("secret"), bytes)) : "invalid";
        return new(fixture.Options, fixture.Client, repository, NullLogger<DocuSignController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = context } };
    }

    [Fact]
    public void Hmac_AuthenticatesRawBytesAndRotatedKeys()
    {
        var body = Encoding.UTF8.GetBytes("{\"mensaje\":\"Autorización\"}");
        var signature = Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes("secret"), body));
        Assert.True(DocuSignHmac.Validate(body, "secret", ["bad", signature]));
        Assert.False(DocuSignHmac.Validate(body, "different", [signature]));
        Assert.False(DocuSignHmac.Validate(Encoding.UTF8.GetBytes("{ \"mensaje\":\"Autorización\"}"), "secret", [signature]));
        Assert.False(DocuSignHmac.Validate(body, "secret", []));
        Assert.False(DocuSignHmac.Validate(body, "", [signature]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Send_RegistersDraftBeforeSendingAndLocksProposalData(int count)
    {
        using var fixture = new Fixture(count);
        var repository = new FakeDocuSignRepository();
        fixture.Handler.BeforeSend = () => Assert.True(repository.Registered);
        var service = fixture.Service(repository);
        var plan = await service.PrepareAsync("C001", default);
        var result = await service.SendAsync(Proposal(), Request(), plan, default);
        Assert.True(result.Enviado);
        Assert.Equal(fixture.Envelope, result.EnvelopeId);
        Assert.Equal(count, repository.Signers.Count);
        Assert.Equal(1, fixture.Handler.CreateCalls);
        Assert.Equal(1, fixture.Handler.SendCalls);
        using var draft = JsonDocument.Parse(fixture.Handler.Draft!);
        Assert.Equal("created", draft.RootElement.GetProperty("status").GetString());
        var signers = draft.RootElement.GetProperty("compositeTemplates")[0].GetProperty("inlineTemplates")[0]
            .GetProperty("recipients").GetProperty("signers");
        Assert.Equal(count, signers.GetArrayLength());
        foreach (var signer in signers.EnumerateArray())
        {
            Assert.Equal("WhatsApp", signer.GetProperty("deliveryMethod").GetString());
            Assert.Equal("1", signer.GetProperty("routingOrder").GetString());
            var tabs = signer.GetProperty("tabs").GetProperty("textTabs");
            Assert.All(tabs.EnumerateArray(), t => Assert.Equal("true", t.GetProperty("locked").GetString()));
            Assert.Contains(tabs.EnumerateArray(), t => t.GetProperty("tabLabel").GetString() == "Valor"
                && t.GetProperty("value").GetString() == "ABC");
        }
    }

    [Fact]
    public async Task Send_WhenSqlFails_DoesNotSendDraft()
    {
        using var fixture = new Fixture(2);
        var service = fixture.Service(new FakeDocuSignRepository { FailRegister = true });
        var plan = await service.PrepareAsync("C001", default);
        var result = await service.SendAsync(Proposal(), Request(), plan, default);
        Assert.False(result.Enviado);
        Assert.NotNull(result.EnvelopeId);
        Assert.Equal(1, fixture.Handler.CreateCalls);
        Assert.Equal(0, fixture.Handler.SendCalls);
    }

    [Fact]
    public async Task Send_LostResponse_DoesNotRepeatSend()
    {
        using var fixture = new Fixture(2);
        fixture.Handler.LoseSendResponse = true;
        var service = fixture.Service(new FakeDocuSignRepository());
        var result = await service.SendAsync(Proposal(), Request(), await service.PrepareAsync("C001", default), default);
        Assert.False(result.Enviado);
        Assert.Equal(1, fixture.Handler.SendCalls);
        Assert.Contains("no cree otra propuesta", result.Advertencia);
    }

    [Fact]
    public async Task Template_RequiresMandatorySignatureForEachRole()
    {
        using var fixture = new Fixture(2);
        fixture.Handler.OptionalSecondSignature = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service(new FakeDocuSignRepository()).PrepareAsync("C001", default));
        Assert.Equal(0, fixture.Handler.CreateCalls);
    }

    [Fact]
    public async Task Status_CompletedRequiresBothKnownRecipientsCompleted()
    {
        using var fixture = new Fixture(2);
        fixture.Handler.SecondStatus = "sent";
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Client.Status(fixture.Tracked(), default));
        fixture.Handler.EnvelopeStatus = "sent";
        var pending = await fixture.Client.Status(fixture.Tracked(), default);
        Assert.Equal("sent", pending.Estado);
        Assert.Equal("completed", pending.Firmantes[0].Estado);
        Assert.Equal("sent", pending.Firmantes[1].Estado);
    }

    [Fact]
    public async Task Archive_PendingAndInvalidPdfDoNotProduceSignedFile()
    {
        using var fixture = new Fixture(1);
        var archive = new DocuSignArchiveService(fixture.Client, fixture.Options);
        fixture.Handler.EnvelopeStatus = "sent";
        await Assert.ThrowsAsync<InvalidOperationException>(() => archive.Download(fixture.Tracked(), default));
        Assert.Equal(0, fixture.Handler.DownloadCalls);
        fixture.Handler.EnvelopeStatus = "completed";
        fixture.Handler.Pdf = "<html>error</html>";
        await Assert.ThrowsAsync<IOException>(() => archive.Download(fixture.Tracked(), default));
        Assert.False(File.Exists(Path.Combine(fixture.Directory, "123.pdf")));
    }

    [Fact]
    public async Task Archive_RetryVerifiesExistingFileAndRecoversInterruptedPublication()
    {
        using var fixture = new Fixture(2);
        var archive = new DocuSignArchiveService(fixture.Client, fixture.Options);
        var first = await archive.Download(fixture.Tracked(), default);
        Assert.Equal("123.pdf", Path.GetFileName(first.Ruta));
        Assert.Equal(first, await archive.Download(fixture.Tracked(), default));
        Assert.Equal(1, fixture.Handler.DownloadCalls);
        File.Delete(first.Ruta); // simula metadatos publicados y proceso interrumpido antes del PDF
        Assert.Equal(first, await archive.Download(fixture.Tracked(), default));
        Assert.Equal(2, fixture.Handler.DownloadCalls);
        await File.WriteAllTextAsync(first.Ruta, "%PDF-modificado");
        await Assert.ThrowsAsync<IOException>(() => archive.Download(fixture.Tracked(), default));
    }

    private static Propuesta Proposal() => new() { PropuestaId = 123, Cosabcli = "C001", Tipo = "Compra", TipoOrden = "Mercado",
        Cantidad = 10, Instrumento = "ABC", Mercado = "BVL", NombreOperador = "Representante", Vigencia = "Hoy" };
    private static PropuestaClienteCreateRequest Request() => new() { Moneda = "USD" };

    private sealed class Contacts(int count) : IClienteFirmantesRepository
    {
        public Task<DocuSignContactos> GetAsync(string code, CancellationToken ct) => Task.FromResult(new DocuSignContactos(
            count == 1 ? [new("Cliente", "Cuenta", "+51999999999")]
                : [new("Titular1", "Cuenta", "+51999999999"), new("Titular2", "Cuenta", "+51988888888")]));
    }

    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "ordenes-docusign-" + Guid.NewGuid().ToString("N"));
        public Guid Account { get; } = Guid.NewGuid();
        public Guid Envelope { get; } = Guid.NewGuid();
        public DocuSignOptions Options { get; }
        public Handler Handler { get; }
        public DocuSignClient Client { get; }
        private readonly HttpClient _http;
        private readonly int _count;
        public Fixture(int count)
        {
            _count = count;
            System.IO.Directory.CreateDirectory(Directory);
            using var rsa = RSA.Create(2048);
            var key = Path.Combine(Directory, "private.key");
            File.WriteAllText(key, rsa.ExportPkcs8PrivateKeyPem());
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DocuSign:IntegrationKey"] = Guid.NewGuid().ToString(), ["DocuSign:UserId"] = Guid.NewGuid().ToString(),
                ["DocuSign:AccountId"] = Account.ToString(), ["DocuSign:AuthServer"] = "account-d.docusign.com",
                ["DocuSign:PrivateKeyPath"] = key, ["DocuSign:IndividualTemplateId"] = Guid.NewGuid().ToString(),
                ["DocuSign:JointTemplateId"] = Guid.NewGuid().ToString(), ["DocuSign:IndividualRole"] = "Cliente",
                ["DocuSign:JointFirstRole"] = "Titular1", ["DocuSign:JointSecondRole"] = "Titular2",
                ["DocuSign:SignedDocumentsPath"] = Directory, ["DocuSign:WebhookUrl"] = "https://example.test/api/DocuSign/webhook",
                ["DocuSign:WebhookHmacSecret"] = "secret"
            }).Build();
            Options = new(config);
            Handler = new(count, Account, Envelope); _http = new(Handler); Client = new(_http, Options);
        }
        public DocuSignPropuestasService Service(FakeDocuSignRepository repo) =>
            new(new Contacts(_count), repo, Client, Options, NullLogger<DocuSignPropuestasService>.Instance);
        public DocuSignSobre Tracked() => new(1, 123, Envelope, Account, "Desarrollo", "completed", _count,
            Enumerable.Range(1, _count).Select(i => new DocuSignFirmante(i.ToString(), _count == 1 ? "Cliente" : $"Titular{i}",
                "Cuenta", i == 1 ? "+51999999999" : "+51988888888", "sent")).ToArray());
        public void Dispose() { Client.Dispose(); _http.Dispose(); System.IO.Directory.Delete(Directory, true); }
    }

    private sealed class Handler(int count, Guid account, Guid envelope) : HttpMessageHandler
    {
        public int CreateCalls, SendCalls, DownloadCalls;
        public string? Draft;
        public Action? BeforeSend;
        public bool LoseSendResponse, OptionalSecondSignature;
        public string EnvelopeStatus = "completed", SecondStatus = "completed", Pdf = "%PDF-1.7\nfake document";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            object data;
            if (path.EndsWith("/oauth/token")) data = new { access_token = "test-token", expires_in = 3600 };
            else if (path.EndsWith("/oauth/userinfo")) data = new { accounts = new[] { new { account_id = account, base_uri = "https://demo.docusign.net" } } };
            else if (path.Contains("/templates/") && path.EndsWith("/recipients")) data = new
            {
                signers = Enumerable.Range(1, count).Select(i => new { recipientId = i.ToString(), roleName = count == 1 ? "Cliente" : $"Titular{i}" })
            };
            else if (path.EndsWith("/tabs")) data = new
            {
                signHereTabs = new[] { new { optional = OptionalSecondSignature && path.Contains("/2/") ? "true" : "false" } },
                textTabs = DocuSignClient.Labels.Select(l => new { tabLabel = l })
            };
            else if (request.Method == HttpMethod.Post && path.EndsWith("/envelopes"))
            { CreateCalls++; Draft = await request.Content!.ReadAsStringAsync(ct); data = new { envelopeId = envelope }; }
            else if (request.Method == HttpMethod.Put)
            {
                SendCalls++; BeforeSend?.Invoke();
                if (LoseSendResponse) throw new HttpRequestException("Respuesta perdida");
                data = new { envelopeId = envelope, status = "sent" };
            }
            else if (path.EndsWith("/recipients")) data = new
            {
                signers = Enumerable.Range(1, count).Select(i => new { recipientId = i.ToString(), status = i == 2 ? SecondStatus : "completed",
                    signedDateTime = "2026-10-05T18:00:00Z" })
            };
            else if (path.EndsWith("/documents/combined"))
            { DownloadCalls++; return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes(Pdf)) }; }
            else data = new { status = EnvelopeStatus, statusChangedDateTime = "2026-10-05T18:00:00Z", completedDateTime = "2026-10-05T18:00:00Z" };
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json") };
        }
    }
}
