using Microsoft.AspNetCore.Mvc;
using OrdenesOnline.Application.Services;
using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;
using OrdenesOnline_API.Models;

namespace OrdenesOnline_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RepresentanteController : ControllerBase
    {
        private readonly RepresentanteService _service;

        public RepresentanteController(RepresentanteService service)
        {
            _service = service;            
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            return Ok(await _service.GetAll());
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(int id)
        {
            var representante = await _service.GetById(id);
            if (representante == null) return NotFound();
            return Ok(representante);
        }

        [HttpPost]
        public async Task<IActionResult> Post(RepresentanteCreateRequest req)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var representante = new Representante
            {
                CorreoCorporativo = req.CorreoCorporativo,
                Password = req.Password,
                Nombre = req.Nombre,
                Cosabcli = req.Cosabcli,
                
            };

            await _service.Add(representante);

                return Ok(new
                {
                    success = true,
                    message = "Representante creado correctamente"
                });
            
        }

        [HttpPut]
        public async Task<IActionResult> Put(Representante representante)
        {
            await _service.Update(representante);
            return Ok();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            await _service.Delete(id);
            return Ok();
        }

        [HttpPost("validate-password")]
        public async Task<IActionResult> ValidatePassword(
        [FromBody] ValidatePasswordRequest request)
        {
            var result = await _service.ValidatePassword(
                request.Correo,
                request.Password
            );

            if (result == null || result.IsValid == 0)
                return Ok(new { isValid = false });

            return Ok(new
            {
                isValid = true,
                userId = result.UserId
            });
        }
    }
}
