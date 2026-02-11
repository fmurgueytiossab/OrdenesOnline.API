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

        public PropuestaController(PropuestaService service, ZapierService zapierService)
        {
            _service = service;
            _zapierService = zapierService;
        }

        //[HttpGet]
        //public async Task<IActionResult> Get()
        //{
        //    return Ok(await _service.GetAll());
        //}

        //[HttpGet("{id}")]
        //public async Task<IActionResult> Get(int id)
        //{
        //    var propuesta = await _service.GetById(id);
        //    if (propuesta == null) return NotFound();
        //    return Ok(propuesta);
        //}

        [HttpPost]
        public async Task<IActionResult> Post(PropuestaCreateRequest req)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var propuesta = new Propuesta
            {
                NombreOperador = req.NombreOperador,
                CorreoCorporativo = req.CorreoCorporativo,
                Cosabcli = req.Cosabcli,
                Tipo = req.Tipo,
                Cantidad = req.Cantidad,
                Instrumento = req.Instrumento,
                TipoOrden = req.TipoOrden,
                Precio = req.Precio,
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


        //[HttpPut]
        //public async Task<IActionResult> Put(Propuesta propuesta)
        //{
        //    await _service.Update(propuesta);
        //    return Ok();
        //}

        //[HttpDelete("{id}")]
        //public async Task<IActionResult> Delete(int id)
        //{
        //    await _service.Delete(id);
        //    return Ok();
        //}
    }
}
