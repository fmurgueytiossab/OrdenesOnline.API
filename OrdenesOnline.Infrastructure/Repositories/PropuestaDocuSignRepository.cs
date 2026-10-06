using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using OrdenesOnline.Domain.interfaces;
using OrdenesOnline.Infrastructure.Persistence;

namespace OrdenesOnline.Infrastructure.Repositories;

// Acceso al SQL existente: no modifica entidades generadas ni AppDbContext.
public sealed class PropuestaDocuSignRepository(AppDbContext context) : IPropuestaDocuSignRepository
{
    private async Task<DbConnection> Connection(CancellationToken ct)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await context.Database.OpenConnectionAsync(ct);
        return connection;
    }
    private static DbCommand Command(DbConnection connection, string sql, DbTransaction? transaction = null,
        params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in parameters)
        {
            var p = command.CreateParameter(); p.ParameterName = name; p.Value = value ?? DBNull.Value;
            command.Parameters.Add(p);
        }
        return command;
    }

    public async Task<long> RegisterAsync(int propuestaId, Guid envelopeId, Guid accountId, string entorno,
        Guid templateId, IReadOnlyList<DocuSignFirmante> firmantes, CancellationToken ct)
    {
        if (firmantes.Count is not (1 or 2)) throw new ArgumentException("Se requiere uno o dos firmantes.");
        var connection = await Connection(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var insert = Command(connection, """
            INSERT dbo.PropuestaDocuSign(PropuestaId,EnvelopeId,AccountId,Entorno,TemplateId,EstadoSobre,FirmasRequeridas)
            OUTPUT INSERTED.PropuestaDocuSignId VALUES(@propuesta,@envelope,@account,@entorno,@template,'created',@count);
            """, transaction, ("@propuesta", propuestaId), ("@envelope", envelopeId), ("@account", accountId),
            ("@entorno", entorno), ("@template", templateId), ("@count", firmantes.Count));
        var id = Convert.ToInt64(await insert.ExecuteScalarAsync(ct));
        foreach (var signer in firmantes)
        {
            await using var command = Command(connection, """
                INSERT dbo.PropuestaDocuSignFirmante(PropuestaDocuSignId,RecipientId,Funcion,Nombre,Celular,IdentificacionTitular,EstadoFirmante)
                VALUES(@id,@recipient,@role,@name,@phone,@identification,'created');
                """, transaction, ("@id", id), ("@recipient", signer.RecipientId), ("@role", signer.Funcion),
                ("@name", signer.Nombre), ("@phone", signer.Celular), ("@identification", signer.Identificacion));
            await command.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return id;
    }

    public async Task<bool> HasAsync(int propuestaId, CancellationToken ct)
    {
        await using var command = Command(await Connection(ct), "SELECT COUNT(*) FROM dbo.PropuestaDocuSign WHERE PropuestaId=@id;",
            null, ("@id", propuestaId));
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct)) > 0;
    }

    public async Task<DocuSignSobre?> FindAsync(Guid envelopeId, Guid accountId, string entorno, CancellationToken ct)
    {
        var connection = await Connection(ct);
        await using var command = Command(connection, """
            SELECT PropuestaDocuSignId,PropuestaId,EstadoSobre,FirmasRequeridas FROM dbo.PropuestaDocuSign
            WHERE EnvelopeId=@envelope AND AccountId=@account AND Entorno=@entorno AND EsVigente=1;
            """, null, ("@envelope", envelopeId), ("@account", accountId), ("@entorno", entorno));
        long id; int proposal; string state; int required;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return null;
            id = reader.GetInt64(0); proposal = reader.GetInt32(1); state = reader.GetString(2); required = reader.GetByte(3);
        }
        await using var signers = Command(connection, """
            SELECT RecipientId,Funcion,Nombre,Celular,EstadoFirmante,FechaFirmaUtc,IdentificacionTitular
            FROM dbo.PropuestaDocuSignFirmante WHERE PropuestaDocuSignId=@id ORDER BY Funcion;
            """, null, ("@id", id));
        var list = new List<DocuSignFirmante>();
        await using var signerReader = await signers.ExecuteReaderAsync(ct);
        while (await signerReader.ReadAsync(ct))
            list.Add(new(signerReader.GetString(0), signerReader.GetString(1), signerReader.GetString(2),
                signerReader.GetString(3), signerReader.GetString(4), signerReader.IsDBNull(5) ? null : signerReader.GetDateTime(5),
                signerReader.IsDBNull(6) ? null : signerReader.GetString(6)));
        return new(id, proposal, envelopeId, accountId, entorno, state, required, list);
    }

    public async Task ApplyAsync(DocuSignSobre sobre, DocuSignEstado estado, CancellationToken ct)
    {
        if (estado.Estado == "completed" && (estado.Firmantes.Count != sobre.FirmasRequeridas
            || estado.Firmantes.Any(s => s.Estado != "completed")
            || sobre.Firmantes.Any(s => estado.Firmantes.Count(a => a.RecipientId == s.RecipientId) != 1)))
            throw new InvalidOperationException("No se puede aceptar una propuesta sin todas las firmas.");
        var connection = await Connection(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        // Bloqueo por sobre, eventos duplicados y consultas concurrentes no hacen retroceder un estado terminal.
        await using var update = Command(connection, """
            UPDATE dbo.PropuestaDocuSign WITH (UPDLOCK,HOLDLOCK)
            SET EstadoSobre=@state,FechaEstadoUtc=@date,
                FechaEnvioUtc=COALESCE(FechaEnvioUtc,@sent),
                FechaCompletadoUtc=CASE WHEN @state='completed' THEN COALESCE(@completed,@date) ELSE FechaCompletadoUtc END
            WHERE PropuestaDocuSignId=@id AND EsVigente=1
              AND (FechaEstadoUtc IS NULL OR FechaEstadoUtc<=@date)
              AND (EstadoSobre NOT IN ('completed','declined','voided') OR EstadoSobre=@state);
            """, transaction, ("@id", sobre.Id), ("@state", estado.Estado), ("@date", estado.FechaEstadoUtc),
            ("@completed", estado.CompletadoUtc), ("@sent", estado.FechaEnvioUtc));
        if (await update.ExecuteNonQueryAsync(ct) == 0) { await transaction.CommitAsync(ct); return; }
        foreach (var s in estado.Firmantes)
        {
            await using var signer = Command(connection, """
                UPDATE dbo.PropuestaDocuSignFirmante
                SET EstadoFirmante=@state,FechaEstadoUtc=@date,FechaFirmaUtc=COALESCE(@signed,FechaFirmaUtc)
                WHERE PropuestaDocuSignId=@id AND RecipientId=@recipient
                  AND (EstadoFirmante NOT IN ('completed','declined') OR EstadoFirmante=@state);
                """, transaction, ("@id", sobre.Id), ("@recipient", s.RecipientId), ("@state", s.Estado),
                ("@date", estado.FechaEstadoUtc), ("@signed", s.FechaFirmaUtc));
            await signer.ExecuteNonQueryAsync(ct);
        }
        if (estado.Estado is "completed" or "declined" or "voided")
        {
            await using var proposal = Command(connection, """
                UPDATE p SET Estado=@state FROM dbo.propuesta p
                JOIN dbo.PropuestaDocuSign d ON d.PropuestaId=p.PropuestaId AND d.EsVigente=1
                WHERE d.PropuestaDocuSignId=@id AND p.Estado='Pendiente'
                  AND (d.EstadoSobre<>'completed' OR
                    (SELECT COUNT(*) FROM dbo.PropuestaDocuSignFirmante f
                      WHERE f.PropuestaDocuSignId=d.PropuestaDocuSignId AND f.EstadoFirmante='completed')=d.FirmasRequeridas);
                """, transaction, ("@id", sobre.Id), ("@state", estado.Estado == "completed" ? "Aceptado" : "Cancelado"));
            await proposal.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<DocuSignSobre>> PendingDownloadsAsync(Guid accountId, string entorno, CancellationToken ct)
    {
        var connection = await Connection(ct);
        var ids = new List<Guid>();
        await using var command = Command(connection, """
            SELECT TOP(20) EnvelopeId FROM dbo.PropuestaDocuSign
            WHERE AccountId=@account AND Entorno=@entorno AND EsVigente=1 AND EstadoSobre='completed'
              AND EstadoDescarga<>'Descargado' AND IntentosDescarga<10 ORDER BY IntentosDescarga,PropuestaDocuSignId;
            """, null, ("@account", accountId), ("@entorno", entorno));
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) ids.Add(reader.GetGuid(0));
        var result = new List<DocuSignSobre>();
        foreach (var id in ids)
            if (await FindAsync(id, accountId, entorno, ct) is { } item) result.Add(item);
        return result;
    }

    public async Task ArchiveAsync(long id, DocuSignArchivo? archivo, string? error, CancellationToken ct)
    {
        await using var command = Command(await Connection(ct), """
            UPDATE dbo.PropuestaDocuSign SET IntentosDescarga=IntentosDescarga+1,
              EstadoDescarga=@state,UbicacionArchivo=@path,Sha256Archivo=@hash,
              FechaDescargaUtc=CASE WHEN @state='Descargado' THEN SYSUTCDATETIME() ELSE NULL END,UltimoError=@error
            WHERE PropuestaDocuSignId=@id AND EsVigente=1 AND EstadoSobre='completed' AND EstadoDescarga<>'Descargado';
            """, null, ("@id", id), ("@state", archivo is null ? "Error" : "Descargado"),
            ("@path", archivo?.Ruta), ("@hash", archivo?.Sha256), ("@error", error));
        await command.ExecuteNonQueryAsync(ct);
    }
}
