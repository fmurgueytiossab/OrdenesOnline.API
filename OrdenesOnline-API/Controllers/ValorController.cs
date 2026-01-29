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
    public class ValorController : ControllerBase
    {
        private readonly ValorService _service;

        public ValorController(ValorService service)
        {
            _service = service;          
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            return Ok(await _service.GetAll());
        }
        
    }
}
