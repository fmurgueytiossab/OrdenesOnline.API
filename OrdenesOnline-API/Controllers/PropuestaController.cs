using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrdenesOnline.Application.Services;
using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;

namespace OrdenesOnline_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PropuestaController : ControllerBase
    {
        private readonly PropuestaService _service;
        private readonly ZapierService _zapierService;
        private readonly RepresentanteService _representanteService;

        public PropuestaController(PropuestaService service, ZapierService zapierService, RepresentanteService representanteService)
        {
            _service = service;
            _zapierService = zapierService;
            _representanteService = representanteService;
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Post(PropuestaCreateRequest req)
        {
            var userIdClaim = User.FindFirst("userId")?.Value;

            if (!int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            var representante = await _representanteService.GetById(userId);

            var propuesta = new Propuesta
            {
                NombreOperador = representante.Nombre,
                CorreoCorporativo = representante.CorreoCorporativo,
                Cosabcli = representante.Cosabcli,
                Tipo = req.Tipo,
                Cantidad = req.Cantidad,
                Instrumento = req.Instrumento,
                TipoOrden = req.TipoOrden,
                Precio = req.Precio,
                Vigencia = req.Vigencia,
                Mercado = req.Mercado,
            };

            await _service.Add(propuesta);

            try
            {
                await _zapierService.EnviarPropuestaCreada(propuesta, req.Dni, req.Moneda);

                return Ok(new
                {
                    success = true,
                    message = "Propuesta creada correctamente"
                });
            }
            catch
            {
                return Ok(new
                {
                    success = true,
                    warning = "La propuesta se guardó, pero no se pudo enviar a Zapier"
                });
            }
        }

    }
}
