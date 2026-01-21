using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrdenesOnline.Application.Services;
using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;
using OrdenesOnline_API.Models;
using System.Security.Claims;

namespace OrdenesOnline_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RepresentanteController : ControllerBase
    {
        private readonly RepresentanteService _service;
        private readonly TokenService _tokenService;

        public RepresentanteController(RepresentanteService service, TokenService tokenService)
        {
            _service = service;
            _tokenService = tokenService;
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
        public async Task<IActionResult> ValidatePassword([FromBody] ValidatePasswordRequest request)
        {
            var result = await _service.ValidatePassword(
                request.Correo,
                request.Password
            );

            if (result == null || result.IsValid == 0)
                return Ok(new { isValid = false });

            var token = _tokenService.GenerateToken(request.Correo, result.UserId);

            return Ok(new
            {
                isValid = true,
                userId = result.UserId,
                token = token
            });
        }

        [HttpGet("validate")]
        public IActionResult ValidateEmail([FromQuery] string token)
        {
            var isValidToken = _tokenService.ValidateToken(token);
            return Ok(isValidToken);
        }

        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var representante = await _service.GetById(int.Parse(userId));

            return Ok(representante);
        }

    }
}
