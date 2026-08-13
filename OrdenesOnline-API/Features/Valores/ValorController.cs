using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrdenesOnline.Application.Services;
using OrdenesOnline.Domain.entities;

namespace OrdenesOnline_API.Features.Valores;

[ApiController]
[Authorize]
[Route("api/Valor")]
public sealed class ValorController : ControllerBase
{
    private readonly ValorService _service;

    public ValorController(ValorService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType<IEnumerable<Valor>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<Valor>>> Get(CancellationToken cancellationToken) =>
        Ok(await _service.GetAll(cancellationToken));
}
