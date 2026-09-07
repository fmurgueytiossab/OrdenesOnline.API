using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrdenesOnline.Application.Services;

namespace OrdenesOnline_API.Features.Propuestas;

[ApiController]
[Authorize]
[Route("api/MarketHours")]
public sealed class MarketHoursController(MarketHoursService service) : ControllerBase
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public ActionResult<MarketHoursSnapshot> Get() => Ok(service.Get());
}
