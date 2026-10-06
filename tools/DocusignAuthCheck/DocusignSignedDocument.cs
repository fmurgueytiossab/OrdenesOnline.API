using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class DocusignSignedDocument
{
    internal const string DefaultDirectory = @"C:\Users\Fernando\Music\docusign";

    public static async Task<int> Run(HttpClient client, string root, string envelopeId, int proposalId,
        bool watch, Func<Task<bool>> refreshToken, string? directory = null)
    {
        if (proposalId <= 0 || !Guid.TryParse(envelopeId, out _)) throw new ArgumentException();
        directory ??= Environment.GetEnvironmentVariable("DOCUSIGN_SIGNED_DOCUMENTS_PATH") ?? DefaultDirectory;
        directory = Path.GetFullPath(directory);
        var pdfPath = Path.Combine(directory, $"{proposalId}.pdf");
        var metadataPath = Path.Combine(directory, $"{proposalId}.docusign.json");
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            // Verifica identidad incluso si una descarga anterior se interrumpio.
            if (File.Exists(metadataPath))
            {
                var saved = JsonSerializer.Deserialize<ArchiveInfo>(await File.ReadAllTextAsync(metadataPath));
                if (saved is null || saved.EnvelopeId != envelopeId || saved.ApiRoot != root || saved.PropuestaId != proposalId)
                    throw new IOException("Conflicto de relacion propuesta/sobre.");
                if (File.Exists(pdfPath))
                {
                    await using var existing = File.OpenRead(pdfPath);
                    var hash = Convert.ToHexString(await SHA256.HashDataAsync(existing));
                    if (hash != saved.Sha256) throw new IOException("El archivo existente no coincide con su huella.");
                    Console.WriteLine($"PDF ya archivado y verificado: {pdfPath}");
                    return 0;
                }
            }
            else if (File.Exists(pdfPath))
            {
                Console.WriteLine("Ya existe un PDF con ese numero, sin relacion verificada con este sobre. No se sobrescribira.");
                return 1;
            }
            while (true)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                using var statusResponse = await client.GetAsync($"{root}/envelopes/{envelopeId}");
                if (!statusResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"No se pudo consultar el sobre (HTTP {(int)statusResponse.StatusCode}).");
                    return 1;
                }
                using var envelope = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
                var status = envelope.RootElement.GetProperty("status").GetString();
                Console.WriteLine($"Estado del sobre: {status}");
                if (status == "completed")
                {
                    var completedAt = envelope.RootElement.TryGetProperty("completedDateTime", out var completed)
                        ? completed.GetString() : null;
                    return await Download(client, root, envelopeId, proposalId, directory, pdfPath, metadataPath, completedAt);
                }
                if (status is "declined" or "voided" || !watch)
                {
                    Console.WriteLine("No se descarga: el sobre no esta completed.");
                    return 1;
                }
                Console.WriteLine("Esperando ambas/todas las firmas. Nueva consulta en 15 minutos. Ctrl+C detiene la espera.");
                await Task.Delay(TimeSpan.FromMinutes(15), cancellation.Token);
                if (!await refreshToken())
                {
                    Console.WriteLine("No se pudo renovar la autenticacion. Puedes reanudar con el mismo comando.");
                    return 1;
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.WriteLine("Espera detenida. Puedes reanudar con el mismo comando.");
            return 0;
        }
        catch (IOException)
        {
            Console.WriteLine("No se pudo archivar: revisa permisos, espacio o conflicto con un PDF/metadatos existentes. No se reemplazo el PDF existente.");
            return 1;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    private static async Task<int> Download(HttpClient client, string root, string envelopeId, int proposalId,
        string directory, string pdfPath, string metadataPath, string? completedAt)
    {
        using var response = await client.GetAsync($"{root}/envelopes/{envelopeId}/documents/combined?certificate=true",
            HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"No se pudo descargar el PDF (HTTP {(int)response.StatusCode}). Puedes repetir la descarga sin reenviar el sobre.");
            return 1;
        }
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{proposalId}.{Guid.NewGuid():N}.tmp");
        var metadataTemp = tempPath + ".json";
        try
        {
            await using (var input = await response.Content.ReadAsStreamAsync())
            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer)) != 0)
                {
                    total += count;
                    if (total > 50 * 1024 * 1024) throw new IOException("El PDF supera el limite de prueba de 50 MB.");
                    await output.WriteAsync(buffer.AsMemory(0, count));
                }
            }
            string hash;
            await using (var downloaded = File.OpenRead(tempPath))
            {
                var magic = new byte[5];
                if (await downloaded.ReadAsync(magic) != 5 || Encoding.ASCII.GetString(magic) != "%PDF-")
                {
                    Console.WriteLine("La descarga no contiene un PDF reconocible. No se guardara como documento firmado.");
                    return 1;
                }
                downloaded.Position = 0;
                hash = Convert.ToHexString(await SHA256.HashDataAsync(downloaded));
            }
            var metadata = new ArchiveInfo(proposalId, envelopeId, root, "completed", completedAt,
                DateTimeOffset.UtcNow, Path.GetFileName(pdfPath), hash);
            await File.WriteAllTextAsync(metadataTemp, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
            // Publica los metadatos primero: permite recuperar una interrupcion sin perder la relacion.
            if (File.Exists(metadataPath))
            {
                var previous = JsonSerializer.Deserialize<ArchiveInfo>(await File.ReadAllTextAsync(metadataPath));
                if (previous is null || previous.EnvelopeId != envelopeId || previous.ApiRoot != root
                    || previous.PropuestaId != proposalId || previous.Sha256 != hash)
                    throw new IOException("Conflicto de metadatos.");
            }
            else File.Move(metadataTemp, metadataPath, overwrite: false);
            File.Move(tempPath, pdfPath, overwrite: false);
            Console.WriteLine($"PDF FIRMADO GUARDADO: {pdfPath}");
            Console.WriteLine("Se guardo la relacion propuesta/sobre y la huella SHA-256 en el archivo .docusign.json.");
            return 0;
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (File.Exists(metadataTemp)) File.Delete(metadataTemp);
        }
    }

    private sealed record ArchiveInfo(int PropuestaId, string EnvelopeId, string ApiRoot, string Estado,
        string? CompletedAt, DateTimeOffset DownloadedAt, string Archivo, string Sha256);
}
