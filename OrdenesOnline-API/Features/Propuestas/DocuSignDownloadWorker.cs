using OrdenesOnline.Application.Services;
using OrdenesOnline.Domain.interfaces;

namespace OrdenesOnline_API.Features.Propuestas;

public sealed class DocuSignDownloadWorker(IServiceScopeFactory scopes, ILogger<DocuSignDownloadWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        // Esperar a que arranque la API; no descarga sobres ajenos a las tablas de esta aplicacion.
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var options = scope.ServiceProvider.GetRequiredService<DocuSignOptions>();
                var repository = scope.ServiceProvider.GetRequiredService<IPropuestaDocuSignRepository>();
                var archive = scope.ServiceProvider.GetRequiredService<DocuSignArchiveService>();
                foreach (var sobre in await repository.PendingDownloadsAsync(options.AccountId, stoppingToken))
                {
                    try
                    {
                        var result = await archive.Download(sobre, stoppingToken);
                        await repository.ArchiveAsync(sobre.Id, result, null, stoppingToken);
                        logger.LogInformation("PDF firmado archivado: propuesta {PropuestaId}, sobre {EnvelopeId}.", sobre.PropuestaId, sobre.EnvelopeId);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        await repository.ArchiveAsync(sobre.Id, null, $"Fallo de descarga: {ex.GetType().Name}. Revise configuracion, permisos y conectividad.", stoppingToken);
                        logger.LogWarning("Descarga DocuSign pendiente: propuesta {PropuestaId}, tipo {ErrorType}.", sobre.PropuestaId, ex.GetType().Name);
                    }
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Worker DocuSign no pudo procesar descargas: {ErrorType}.", ex.GetType().Name);
            }
        }
    }
}
