using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OrdenesOnline.Domain.interfaces;

namespace OrdenesOnline.Application.Services;

public sealed class DocuSignArchiveService(DocuSignClient client, DocuSignOptions options)
{
    private sealed record Metadata(int PropuestaId, Guid EnvelopeId, Guid AccountId, string Sha256);

    public async Task<DocuSignArchivo> Download(DocuSignSobre sobre, CancellationToken ct)
    {
        // Verificar de nuevo en el servidor antes de descargar incluso tras un reinicio del proceso.
        var status = await client.Status(sobre, ct);
        if (status.Estado != "completed") throw new InvalidOperationException("El sobre no esta completado.");
        var directory = options.SignedDocumentsPath;
        Directory.CreateDirectory(directory);
        var pdf = Path.Combine(directory, $"{sobre.PropuestaId}.pdf");
        var metadataPath = Path.Combine(directory, $"{sobre.PropuestaId}.docusign.json");
        Metadata? metadata = null;
        if (File.Exists(metadataPath))
        {
            metadata = JsonSerializer.Deserialize<Metadata>(await File.ReadAllTextAsync(metadataPath, ct));
            if (metadata is null || metadata.EnvelopeId != sobre.EnvelopeId || metadata.AccountId != sobre.AccountId
                || metadata.PropuestaId != sobre.PropuestaId)
                throw new IOException("Conflicto entre archivo y sobre.");
            if (File.Exists(pdf))
            {
                await using var existing = File.OpenRead(pdf);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(existing, ct));
                if (hash != metadata.Sha256) throw new IOException("La huella del PDF existente no coincide.");
                return new(pdf, hash);
            }
        }
        else if (File.Exists(pdf)) throw new IOException("Existe un PDF sin relacion verificada con el sobre.");
        var temporary = Path.Combine(directory, $".{sobre.PropuestaId}.{Guid.NewGuid():N}.tmp");
        var temporaryMetadata = temporary + ".json";
        try
        {
            using var response = await client.Request(HttpMethod.Get,
                $"/envelopes/{sobre.EnvelopeId}/documents/combined?certificate=true", null, ct, HttpCompletionOption.ResponseHeadersRead);
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920]; long total = 0; int count;
                while ((count = await input.ReadAsync(buffer, ct)) > 0)
                {
                    total += count;
                    if (total > 50 * 1024 * 1024) throw new IOException("El documento supera 50 MB.");
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                }
                await output.FlushAsync(ct);
            }
            string hash;
            await using (var downloaded = File.OpenRead(temporary))
            {
                var magic = new byte[5];
                await downloaded.ReadExactlyAsync(magic, ct);
                if (Encoding.ASCII.GetString(magic) != "%PDF-") throw new IOException("La descarga no es un PDF.");
                downloaded.Position = 0;
                hash = Convert.ToHexString(await SHA256.HashDataAsync(downloaded, ct));
            }
            if (metadata is not null && metadata.Sha256 != hash) throw new IOException("La descarga no coincide con los metadatos anteriores.");
            if (metadata is null)
            {
                await File.WriteAllTextAsync(temporaryMetadata,
                    JsonSerializer.Serialize(new Metadata(sobre.PropuestaId, sobre.EnvelopeId, sobre.AccountId, hash)), ct);
                // Metadatos primero para recuperar una interrupcion sin perder la relacion con el sobre.
                File.Move(temporaryMetadata, metadataPath, false);
            }
            File.Move(temporary, pdf, false);
            return new(pdf, hash);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (File.Exists(temporaryMetadata)) File.Delete(temporaryMetadata);
        }
    }
}
