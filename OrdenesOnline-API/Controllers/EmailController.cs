using Microsoft.AspNetCore.Mvc;
using OrdenesOnline.Application.Services;
using OrdenesOnline.Domain.DTO;

namespace OrdenesOnline_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EmailController : ControllerBase
    {
        private readonly EmailService _emailService;
        private readonly TokenService _tokenService;
        private readonly RepresentanteService _representanteService;

        public EmailController(EmailService emailService, TokenService tokenService, RepresentanteService representanteService)
        {
            _emailService = emailService;
            _tokenService = tokenService;
            _representanteService = representanteService;
        }

        [HttpPost("send-validation")]
        public async Task<IActionResult> SendValidationEmail([FromBody] SendValidationEmailRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email))
                return BadRequest("Debe ingresar un correo válido");

            try
            {
                // Buscar cliente existente
                var clienteExistente = await _representanteService.GetByEmail(request.Email);

                if (clienteExistente != null)
                {
                    var token = _tokenService.GenerateToken(request.Email, clienteExistente.UserId);

                    var expiration = DateTime.UtcNow.AddMinutes(60);

                    var link = $"https://10.80.1.15/OrdenesOnline/change-password?token={token}";

                    var html = $@"
                <p>Para poder obtener una nueva contraseña haga click aqui:</p>
                <a href='{link}' style='color: #1a73e8; font-size: 16px;'>Nueva contraseña</a>";

                    // Enviar correo
                    await _emailService.SendEmailAsync(request.Email, "Validación", html);
                }

                // Siempre retornar OK, aunque cliente no exista
                return Ok(new
                {
                    mensaje = "Si el correo existe, se enviaron instrucciones para restablecer la contraseña"
                });
            }
            catch (Exception ex)
            {
                // Log interno del error para debug, pero no lo mostramos al usuario
                Console.WriteLine(ex);

                // Retornar un error técnico solo si falla SMTP u otro fallo grave
                return StatusCode(500, new { mensaje = "No se pudo procesar la solicitud. Intente más tarde." });
            }
        }


        [HttpGet("validate")]
        public IActionResult ValidateEmail([FromQuery] string token)
        {
            var email = _tokenService.ValidateToken(token);

            if (email == null)
                return BadRequest("Token inválido o expirado");

            return Ok(new { email = email });
        }
    }
}
