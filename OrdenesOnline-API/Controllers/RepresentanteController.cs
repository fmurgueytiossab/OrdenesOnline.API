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

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDTO request)
        {
            var result = await _service.Login(
                request.Correo,
                request.Password
            );

            if (result == null || result.IsValid == 0)
                return Ok(new { isValid = false });

            var token = _tokenService.GenerateToken(request.Correo, result.RepresentanteId);

            return Ok(new
            {
                isValid = true,
                userId = result.RepresentanteId,
                token = token
            });
        }

        [HttpPost("update-password")]
        public async Task<IActionResult> UpdatePassword([FromBody] UpdatePasswordByToken request)
        {
            var result = await _service.UpdatePasswordByToken(
                request.Token,
                request.Password
            );

            return Ok(new { isValid = result, mensaje = result ? "Contraseña cambiada" : "Error al cambiar" });
        }


        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            var representanteId = User.FindFirst("userId")?.Value;

            if (representanteId == null)
                return Unauthorized();

            var representante = await _service.GetById(int.Parse(representanteId));
            return Ok(representante);
        }


    }
}
