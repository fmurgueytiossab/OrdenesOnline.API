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
    [InlineData("{\"error\":\"consent_required\"}", "DocuSign HTTP 400 consent_required")]
    [InlineData("{\"error\":\"invalid_grant\",\"error_description\":\"no_valid_keys_or_signatures\"}", "DocuSign HTTP 400 invalid_grant no_valid_keys_or_signatures")]
    [InlineData("{\"errorCode\":\"USER_NOT_FOUND\"}", "DocuSign HTTP 400 USER_NOT_FOUND")]
    [InlineData("{\"error\":\"invalid_grant\",\"error_description\":\"Bearer secret-token\\nprivate key\",\"access_token\":\"secret-token\"}", "DocuSign HTTP 400 invalid_grant")]
    [InlineData("{\"error\":42,\"error_description\":{\"secret\":\"private key\"}}", "DocuSign HTTP 400")]
    [InlineData("<html>proxy error</html>", "DocuSign HTTP 400")]
    [InlineData("[]", "DocuSign HTTP 400")]
    public async Task AuthenticationFailure_ReportsSafeProviderIdentifiersWithoutResponseBody(string body, string message)
    {
        using var fixture = new Fixture(1);
        fixture.Handler.AuthError = body;
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            fixture.Client.Request(HttpMethod.Get, "/templates/test/recipients", null, default));
        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal(message, exception.Message);
        Assert.Equal(0, fixture.Handler.CreateCalls);
        Assert.Equal(0, fixture.Handler.SendCalls);
    }

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

    [Fact]
    public void JointContacts_ThreeHoldersKeepThreeDistinctNormalizedPhones()
    {
        var result = ClienteFirmantesRepository.Resolve("Cuenta de tres", "M", "999111111",
            ["+51 999-999-999", "988888888", "051977777777"]);
        Assert.Null(result.Error);
        Assert.Equal(new[] { "Titular1", "Titular2", "Titular3" }, result.Firmantes.Select(f => f.Funcion));
        Assert.Equal(new[] { "+51977777777", "+51988888888", "+51999999999" }, result.Firmantes.Select(f => f.Celular));
        Assert.All(result.Firmantes, f => Assert.Equal("Cuenta de tres", f.Nombre));
    }

    [Theory]
    [InlineData("999999999", "988888888", "51999999999")]
    [InlineData("999999999", "988888888", "invalido")]
    public void JointContacts_ThreeHoldersRejectDuplicateOrInvalidPhones(string first, string second, string third)
    {
        var result = ClienteFirmantesRepository.Resolve("Cuenta", "S", null, [first, second, third]);
        Assert.NotNull(result.Error); Assert.Empty(result.Firmantes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void JointContacts_UnsupportedHolderCountIsRejected(int count)
    {
        var result = ClienteFirmantesRepository.Resolve("Cuenta", "S", null,
            Enumerable.Range(1, count).Select(i => (string?)$"9{i:00000000}").ToArray());
        Assert.NotNull(result.Error); Assert.Empty(result.Firmantes);
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

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Prepare_SelectsTemplateForExactSignerCount(int count)
    {
        using var fixture = new Fixture(count);
        var plan = await fixture.Service(new FakeDocuSignRepository()).PrepareAsync("C001", default);
        var expected = count switch { 1 => fixture.Options.IndividualTemplateId, 2 => fixture.Options.JointTemplateId,
            _ => fixture.Options.JointThreeTemplateId!.Value };
        Assert.Equal(expected, plan.TemplateId);
        Assert.Equal(count, plan.Firmantes.Count);
        Assert.Equal("Titular3", fixture.Options.JointThirdRole);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Prepare_ExistingCasesWorkWithoutThirdTemplate(int count)
    {
        using var fixture = new Fixture(count, configureThreeTemplate: false);
        Assert.Null(fixture.Options.JointThreeTemplateId);
        Assert.Equal(count, (await fixture.Service(new FakeDocuSignRepository()).PrepareAsync("C001", default)).Firmantes.Count);
    }

    [Fact]
    public async Task Prepare_ThreeSignersRequireConfigurationBeforeCreatingEnvelope()
    {
        using var fixture = new Fixture(3, configureThreeTemplate: false);
        var error = await Assert.ThrowsAsync<DocuSignContactException>(() =>
            fixture.Service(new FakeDocuSignRepository()).PrepareAsync("C001", default));
        Assert.Contains("DocuSign:JointThreeTemplateId", error.Message);
        Assert.Equal(0, fixture.Handler.CreateCalls);
        Assert.Equal(0, fixture.Handler.SendCalls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Webhook_DecliningAnyOfThreeHoldersStopsApproval(int holder)
    {
        using var fixture = new Fixture(3);
        fixture.Handler.EnvelopeStatus = "declined";
        fixture.Handler.FirstStatus = holder == 1 ? "declined" : "completed";
        fixture.Handler.SecondStatus = holder == 2 ? "declined" : "completed";
        fixture.Handler.ThirdStatus = holder == 3 ? "declined" : "sent";
        var repository = new FakeDocuSignRepository { Found = fixture.Tracked() };
        Assert.IsType<OkResult>(await WebhookController(fixture, repository, true).Webhook(default));
        Assert.Equal("declined", repository.Applied?.Estado);
        Assert.Equal("declined", repository.Applied?.Firmantes[holder - 1].Estado);
    }

    [Fact]
    public async Task Status_TwoOfThreeSignaturesCannotCompleteProposal()
    {
        using var fixture = new Fixture(3);
        fixture.Handler.ThirdStatus = "sent";
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Client.Status(fixture.Tracked(), default));
        fixture.Handler.EnvelopeStatus = "sent";
        var pending = await fixture.Client.Status(fixture.Tracked(), default);
        Assert.Equal(2, pending.Firmantes.Count(s => s.Estado == "completed"));
        fixture.Handler.EnvelopeStatus = "completed"; fixture.Handler.ThirdStatus = "completed";
        Assert.Equal("completed", (await fixture.Client.Status(fixture.Tracked(), default)).Estado);
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
    [InlineData(3)]
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
        Assert.Contains("«Aprobar» o «Declinar»", draft.RootElement.GetProperty("emailBlurb").GetString());
        var signers = draft.RootElement.GetProperty("compositeTemplates")[0].GetProperty("inlineTemplates")[0]
            .GetProperty("recipients").GetProperty("signers");
        Assert.Equal(count, signers.GetArrayLength());
        foreach (var signer in signers.EnumerateArray())
        {
            Assert.Equal("WhatsApp", signer.GetProperty("deliveryMethod").GetString());
            Assert.Equal("1", signer.GetProperty("routingOrder").GetString());
            var tabs = signer.GetProperty("tabs").GetProperty("textTabs");
            Assert.All(tabs.EnumerateArray(), t => Assert.Equal("true", t.GetProperty("locked").GetString()));
            var values = tabs.EnumerateArray().ToDictionary(t => t.GetProperty("tabLabel").GetString()!,
                t => t.GetProperty("value").GetString());
            Assert.Equal(DocuSignClient.TemplateLabels(count > 1).Order(), values.Keys.Order());
            Assert.Equal("Compra", values["TipoOperacion"]);
            Assert.Equal(count == 1 ? "Cliente Prueba" : "Cuenta Juan y Miguel", values[count == 1 ? "NombreCliente" : "NombreCuenta"]);
            Assert.Equal("C001", values[count == 1 ? "CodigoCliente" : "CodigoCuenta"]);
            Assert.Equal("No aplica", values["Monto"]);
            Assert.Equal("A mercado", values["Precio"]);
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

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Send_RenumberedRecipientsAreRegisteredByRoleAndWebhookCompletes(int count)
    {
        using var fixture = new Fixture(count);
        fixture.Handler.TemplateRecipientIds = ["30432997", "97531", "87654"];
        fixture.Handler.ReverseEnvelopeRecipients = true;
        var repository = new FakeDocuSignRepository();
        fixture.Handler.BeforeSend = () =>
        {
            Assert.True(repository.Registered);
            Assert.Equal(Enumerable.Range(1, count).Select(i => i.ToString()), repository.Signers.Select(s => s.RecipientId));
        };
        var service = fixture.Service(repository);
        var result = await service.SendAsync(Proposal(), Request(), await service.PrepareAsync("C001", default), default);
        Assert.True(result.Enviado);
        Assert.Equal(count, repository.Signers.Count);
        Assert.Equal(fixture.Tracked().Firmantes.Select(s => s.Funcion), repository.Signers.Select(s => s.Funcion));
        using var draft = JsonDocument.Parse(fixture.Handler.Draft!);
        var inline = draft.RootElement.GetProperty("compositeTemplates")[0].GetProperty("inlineTemplates")[0]
            .GetProperty("recipients").GetProperty("signers");
        Assert.Equal("30432997", inline[0].GetProperty("recipientId").GetString());
        repository.Found = fixture.Tracked() with { Estado = "created", Firmantes = repository.Signers };
        Assert.IsType<OkResult>(await WebhookController(fixture, repository, true).Webhook(default));
        Assert.Equal("completed", repository.Applied?.Estado);
        Assert.All(repository.Applied!.Firmantes, s => Assert.Equal("completed", s.Estado));
        Assert.Equal(1, fixture.Handler.CreateCalls);
        Assert.Equal(1, fixture.Handler.SendCalls);
    }

    [Theory]
    [InlineData("missing-role")]
    [InlineData("duplicate-role")]
    [InlineData("duplicate-id")]
    [InlineData("empty-id")]
    [InlineData("extra-signer")]
    public async Task Send_AmbiguousDraftRecipientsAreNotRegisteredOrSent(string fault)
    {
        using var fixture = new Fixture(2);
        fixture.Handler.EnvelopeRecipientFault = fault;
        var repository = new FakeDocuSignRepository();
        var service = fixture.Service(repository);
        var result = await service.SendAsync(Proposal(), Request(), await service.PrepareAsync("C001", default), default);
        Assert.False(result.Enviado);
        Assert.NotNull(result.EnvelopeId);
        Assert.False(repository.Registered);
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

    [Theory]
    [InlineData(1, "approveTabs", "Aprobar")]
    [InlineData(1, "declineTabs", "Declinar")]
    [InlineData(2, "approveTabs", "AprobarTitular2")]
    [InlineData(2, "declineTabs", "DeclinarTitular2")]
    [InlineData(3, "approveTabs", "AprobarTitular3")]
    [InlineData(3, "declineTabs", "DeclinarTitular3")]
    public async Task Template_RequiresBothNativeDecisionButtonsForEachRecipient(int count, string missing, string label)
    {
        using var fixture = new Fixture(count);
        fixture.Handler.MissingDecision = missing;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service(new FakeDocuSignRepository()).PrepareAsync("C001", default));
        Assert.Contains(label, error.Message);
        Assert.Equal(0, fixture.Handler.CreateCalls);
    }

    [Theory]
    [InlineData("wrong-label")]
    [InlineData("wrong-recipient")]
    [InlineData("conditional")]
    [InlineData("duplicate")]
    [InlineData("signature-only")]
    public async Task Template_RejectsInvalidOrMisassignedDecisionButtons(string fault)
    {
        using var fixture = new Fixture(2);
        fixture.Handler.DecisionFault = fault;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service(new FakeDocuSignRepository()).PrepareAsync("C001", default));
        Assert.Equal(0, fixture.Handler.CreateCalls);
        Assert.Equal(0, fixture.Handler.SendCalls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Webhook_DecliningEitherHolderStopsJointApproval(int holder)
    {
        using var fixture = new Fixture(2);
        fixture.Handler.EnvelopeStatus = "declined";
        fixture.Handler.FirstStatus = holder == 1 ? "declined" : "completed";
        fixture.Handler.SecondStatus = holder == 2 ? "declined" : "sent";
        var repository = new FakeDocuSignRepository { Found = fixture.Tracked() };
        Assert.IsType<OkResult>(await WebhookController(fixture, repository, true).Webhook(default));
        Assert.Equal("declined", repository.Applied?.Estado);
        Assert.Equal("declined", repository.Applied?.Firmantes[holder - 1].Estado);
    }

    [Theory]
    [InlineData("Texto")]
    [InlineData("")]
    [InlineData("Operacion")]
    [InlineData("TipoOrden")]
    [InlineData("NombreCuenta")]
    public async Task Template_IdentifiesUnexpectedTextLabelWithoutLoggingItsValue(string label)
    {
        using var fixture = new Fixture(1);
        fixture.Handler.UnexpectedTextLabel = label;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service(new FakeDocuSignRepository()).PrepareAsync("C001", default));
        Assert.Contains(JsonSerializer.Serialize(label), error.Message);
        Assert.Contains("funcion Cliente", error.Message);
        Assert.DoesNotContain("valor privado del campo", error.Message);
        Assert.Equal(0, fixture.Handler.CreateCalls);
    }

    [Fact]
    public async Task Template_IdentifiesMissingRequiredTextLabel()
    {
        using var fixture = new Fixture(1);
        fixture.Handler.MissingTextLabel = "CodigoCliente";
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service(new FakeDocuSignRepository()).PrepareAsync("C001", default));
        Assert.Contains("Faltan etiquetas de datos en la plantilla: CodigoCliente.", error.Message);
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

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Download_ReturnsPdfForAuthorizedClientWithoutCreatingFilesOrUpdatingSql(int count)
    {
        using var fixture = new Fixture(count);
        var repository = new FakeDocuSignRepository { Found = fixture.Completed() };
        var controller = DocumentoController(fixture, repository);
        var filesBefore = Directory.GetFiles(fixture.Directory);
        var file = Assert.IsType<FileContentResult>(await controller.Download(123, default));
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("Propuesta-123.pdf", file.FileDownloadName);
        Assert.Equal(fixture.Handler.Pdf, Encoding.ASCII.GetString(file.FileContents));
        Assert.Equal(0, repository.ApplyCalls);
        Assert.Equal(filesBefore, Directory.GetFiles(fixture.Directory));
        Assert.IsType<FileContentResult>(await controller.Download(123, default));
        Assert.Equal(2, fixture.Handler.DownloadCalls); // Cada clic consulta al proveedor; no hay cache local.
    }

    [Theory]
    [InlineData(false, "OTRO", 404)]
    [InlineData(true, "C001", 401)]
    public async Task Download_EnforcesRepresentativeClientScopeBeforeCallingDocuSign(bool missingRepresentative,
        string client, int status)
    {
        using var fixture = new Fixture(1);
        var repository = new FakeDocuSignRepository { Found = fixture.Completed() };
        var controller = DocumentoController(fixture, repository,
            new FakeRepresentanteClientScopeRepository(!missingRepresentative, clientCodes: [client]));
        var response = Assert.IsType<ObjectResult>(await controller.Download(123, default));
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(0, repository.FindByProposalCalls);
        Assert.Equal(0, fixture.Handler.RequestCalls);
    }

    [Fact]
    public async Task Download_MissingIdentityDoesNotReadProposalOrProvider()
    {
        using var fixture = new Fixture(1);
        var repository = new FakeDocuSignRepository();
        var controller = DocumentoController(fixture, repository);
        controller.HttpContext.User = new();
        Assert.Equal(401, Assert.IsType<ObjectResult>(await controller.Download(123, default)).StatusCode);
        Assert.Equal(0, repository.FindByProposalCalls);
        Assert.Equal(0, fixture.Handler.RequestCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Download_MissingOrOtherAccountEnvelopeReturns404(bool otherAccount)
    {
        using var fixture = new Fixture(1);
        var repository = new FakeDocuSignRepository
        {
            Found = otherAccount ? fixture.Completed() with { AccountId = Guid.NewGuid() } : null
        };
        Assert.Equal(404, Assert.IsType<ObjectResult>(await DocumentoController(fixture, repository)
            .Download(123, default)).StatusCode);
        Assert.Equal(0, fixture.Handler.RequestCalls);
    }

    [Theory]
    [InlineData("sent")]
    [InlineData("declined")]
    [InlineData("voided")]
    public async Task Download_RejectsNonCompletedLocalEnvelope(string state)
    {
        using var fixture = new Fixture(2);
        var repository = new FakeDocuSignRepository { Found = fixture.Completed() with { Estado = state } };
        Assert.Equal(409, Assert.IsType<ObjectResult>(await DocumentoController(fixture, repository)
            .Download(123, default)).StatusCode);
        Assert.Equal(0, fixture.Handler.RequestCalls);
    }

    [Fact]
    public async Task Download_RequiresAllLocalAndRemoteSignatures()
    {
        using var fixture = new Fixture(3);
        var repository = new FakeDocuSignRepository { Found = fixture.Tracked() };
        var controller = DocumentoController(fixture, repository);
        Assert.Equal(409, Assert.IsType<ObjectResult>(await controller.Download(123, default)).StatusCode);
        Assert.Equal(0, fixture.Handler.RequestCalls);
        repository.Found = fixture.Completed();
        fixture.Handler.EnvelopeStatus = "sent"; fixture.Handler.ThirdStatus = "sent";
        Assert.Equal(409, Assert.IsType<ObjectResult>(await controller.Download(123, default)).StatusCode);
        fixture.Handler.EnvelopeStatus = "completed";
        Assert.Equal(502, Assert.IsType<ObjectResult>(await controller.Download(123, default)).StatusCode);
        Assert.Equal(0, fixture.Handler.DownloadCalls);
    }

    [Theory]
    [InlineData("invalid-pdf")]
    [InlineData("http-error")]
    [InlineData("oversized")]
    [InlineData("chunked-oversized")]
    [InlineData("timeout")]
    public async Task Download_ProviderFailureDoesNotReturnAnAttachment(string failure)
    {
        using var fixture = new Fixture(1);
        fixture.Handler.DownloadFailure = failure;
        var repository = new FakeDocuSignRepository { Found = fixture.Completed() };
        Assert.Equal(502, Assert.IsType<ObjectResult>(await DocumentoController(fixture, repository)
            .Download(123, default)).StatusCode);
        Assert.Equal(0, repository.ApplyCalls);
        Assert.Single(Directory.GetFiles(fixture.Directory)); // Solo la clave RSA efimera del fixture.
    }

    [Fact]
    public async Task Download_CancellationIsPropagated()
    {
        using var fixture = new Fixture(1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Client.DownloadCombinedPdf(fixture.Envelope, cancellation.Token));
        Assert.Equal(0, fixture.Handler.DownloadCalls);
    }

    private static PropuestaDocumentoController DocumentoController(Fixture fixture, FakeDocuSignRepository repository,
        IRepresentanteClientScopeRepository? scope = null)
    {
        var service = new DocuSignDocumentoService(new ReadOnlyProposalRepository(),
            scope ?? new FakeRepresentanteClientScopeRepository(), repository, fixture.Client, fixture.Options);
        var context = new DefaultHttpContext
        {
            User = new(new System.Security.Claims.ClaimsIdentity([new("sub", "7")], "test"))
        };
        return new(service, NullLogger<PropuestaDocumentoController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = context } };
    }

    private sealed class ReadOnlyProposalRepository : IPropuestaRepository
    {
        public Task<Propuesta?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Propuesta?>(id == 123 ? Proposal() : null);
        public Task<IEnumerable<Propuesta>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAsync(Propuesta p, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(Propuesta p, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static Propuesta Proposal() => new() { PropuestaId = 123, Cosabcli = "C001", Tipo = "Compra", TipoOrden = "Mercado",
        Cantidad = 10, Instrumento = "ABC", Mercado = "BVL", NombreOperador = "Representante", Vigencia = "Hoy" };
    private static PropuestaClienteCreateRequest Request() => new() { Moneda = "USD" };

    private sealed class Contacts(int count) : IClienteFirmantesRepository
    {
        public Task<DocuSignContactos> GetAsync(string code, CancellationToken ct) => Task.FromResult(new DocuSignContactos(
            count == 1 ? [new("Cliente", "Cliente Prueba", "+51999999999")]
                : Enumerable.Range(1, count).Select(i => new DocuSignContacto($"Titular{i}", "Cuenta Juan y Miguel",
                    new[] { "+51999999999", "+51988888888", "+51977777777" }[i - 1])).ToArray()));
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
        public Fixture(int count, bool configureThreeTemplate = true)
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
                ["DocuSign:WebhookUrl"] = "https://example.test/api/DocuSign/webhook",
                ["DocuSign:WebhookHmacSecret"] = "secret"
            }).Build();
            if (configureThreeTemplate) config["DocuSign:JointThreeTemplateId"] = Guid.NewGuid().ToString();
            Options = new(config);
            Handler = new(count, Account, Envelope); _http = new(Handler); Client = new(_http, Options);
        }
        public DocuSignPropuestasService Service(FakeDocuSignRepository repo) =>
            new(new Contacts(_count), repo, Client, Options, NullLogger<DocuSignPropuestasService>.Instance);
        public DocuSignSobre Tracked() => new(1, 123, Envelope, Account, "completed", _count,
            Enumerable.Range(1, _count).Select(i => new DocuSignFirmante(i.ToString(), _count == 1 ? "Cliente" : $"Titular{i}",
                "Cuenta", new[] { "+51999999999", "+51988888888", "+51977777777" }[i - 1], "sent")).ToArray());
        public DocuSignSobre Completed() => Tracked() with
        { Firmantes = Tracked().Firmantes.Select(s => s with { Estado = "completed" }).ToArray() };
        public void Dispose() { Client.Dispose(); _http.Dispose(); System.IO.Directory.Delete(Directory, true); }
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private sealed class Handler(int count, Guid account, Guid envelope) : HttpMessageHandler
    {
        public int CreateCalls, SendCalls, DownloadCalls, RequestCalls;
        public string? DownloadFailure;
        public string? Draft;
        public string? AuthError;
        public Action? BeforeSend;
        public bool LoseSendResponse;
        public string? MissingDecision, DecisionFault;
        public string? UnexpectedTextLabel, MissingTextLabel;
        public string[] TemplateRecipientIds = Enumerable.Range(1, count).Select(i => i.ToString()).ToArray();
        public bool ReverseEnvelopeRecipients;
        public string? EnvelopeRecipientFault;
        public string EnvelopeStatus = "completed", FirstStatus = "completed", SecondStatus = "completed", ThirdStatus = "completed", Pdf = "%PDF-1.7\nfake document";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestCalls++;
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/oauth/token") && AuthError is not null)
                return new(HttpStatusCode.BadRequest) { Content = new StringContent(AuthError, Encoding.UTF8, "application/json") };
            object data;
            if (path.EndsWith("/oauth/token")) data = new { access_token = "test-token", expires_in = 3600 };
            else if (path.EndsWith("/oauth/userinfo")) data = new { accounts = new[] { new { account_id = account, base_uri = "https://demo.docusign.net" } } };
            else if (path.Contains("/templates/") && path.EndsWith("/recipients")) data = new
            {
                signers = Enumerable.Range(1, count).Select(i => new { recipientId = TemplateRecipientIds[i - 1], roleName = count == 1 ? "Cliente" : $"Titular{i}" })
            };
            else if (path.EndsWith("/tabs"))
            {
                var index = Array.FindIndex(TemplateRecipientIds, id => path.Contains($"/{id}/"));
                var id = TemplateRecipientIds[index];
                var suffix = count == 1 ? "" : $"Titular{index + 1}";
                var fault = index == count - 1;
                object[] Button(string prefix) =>
                    Enumerable.Range(0, fault && DecisionFault == "duplicate" ? 2 : 1).Select(_ => (object)new
                    {
                        tabLabel = prefix + (fault && DecisionFault == "wrong-label" ? "Titular1" : suffix),
                        recipientId = fault && DecisionFault == "wrong-recipient" ? "1" : id,
                        conditionalParentLabel = fault && DecisionFault == "conditional" ? "Seleccion" : ""
                    }).ToArray();
                data = new
                {
                    // Sin signHereTabs: debe admitir la decisión nativa sin una firma visible.
                    approveTabs = fault && (MissingDecision == "approveTabs" || DecisionFault == "signature-only") ? [] : Button("Aprobar"),
                    declineTabs = fault && (MissingDecision == "declineTabs" || DecisionFault == "signature-only") ? [] : Button("Declinar"),
                    signHereTabs = DecisionFault == "signature-only" ? new[] { new { optional = "false" } } : [],
                    textTabs = DocuSignClient.TemplateLabels(count > 1).Where(l => l != MissingTextLabel)
                        .Concat(UnexpectedTextLabel is null ? [] : new[] { UnexpectedTextLabel })
                        .Select(l => new { tabLabel = l, value = "valor privado del campo" })
                };
            }
            else if (request.Method == HttpMethod.Post && path.EndsWith("/envelopes"))
            { CreateCalls++; Draft = await request.Content!.ReadAsStringAsync(ct); data = new { envelopeId = envelope }; }
            else if (request.Method == HttpMethod.Put)
            {
                SendCalls++; BeforeSend?.Invoke();
                if (LoseSendResponse) throw new HttpRequestException("Respuesta perdida");
                data = new { envelopeId = envelope, status = "sent" };
            }
            else if (path.EndsWith("/recipients"))
            {
                var indices = Enumerable.Range(1, count + (EnvelopeRecipientFault == "extra-signer" ? 1 : 0));
                if (ReverseEnvelopeRecipients) indices = indices.Reverse();
                data = new
                {
                    signers = indices.Select(i => new
                    {
                        recipientId = i == count && EnvelopeRecipientFault == "duplicate-id" ? "1"
                            : i == count && EnvelopeRecipientFault == "empty-id" ? "" : i.ToString(),
                        roleName = i == count && EnvelopeRecipientFault == "missing-role" ? ""
                            : i == count && EnvelopeRecipientFault == "duplicate-role" ? "Titular1"
                            : count == 1 ? "Cliente" : $"Titular{i}",
                        status = i switch { 1 => FirstStatus, 2 => SecondStatus, _ => ThirdStatus },
                        signedDateTime = "2026-10-05T18:00:00Z"
                    })
                };
            }
            else if (path.EndsWith("/documents/combined"))
            {
                DownloadCalls++;
                Assert.Equal("?certificate=true", request.RequestUri.Query);
                if (DownloadFailure == "timeout") throw new OperationCanceledException("Timeout del proveedor");
                if (DownloadFailure == "http-error") return new(HttpStatusCode.ServiceUnavailable)
                { Content = new StringContent("{\"errorCode\":\"DOCUMENT_UNAVAILABLE\"}") };
                var content = DownloadFailure == "chunked-oversized"
                    ? (HttpContent)new StreamContent(new NonSeekableStream(new byte[50 * 1024 * 1024 + 1]))
                    : new ByteArrayContent(Encoding.ASCII.GetBytes(DownloadFailure == "invalid-pdf" ? "<html>error</html>" : Pdf));
                if (DownloadFailure == "oversized") content.Headers.ContentLength = 50 * 1024 * 1024 + 1;
                if (DownloadFailure == "chunked-oversized") content.Headers.ContentLength = null;
                return new(HttpStatusCode.OK) { Content = content };
            }
            else data = new { status = EnvelopeStatus, statusChangedDateTime = "2026-10-05T18:00:00Z", completedDateTime = "2026-10-05T18:00:00Z" };
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json") };
        }
    }
}
