using System;
using System.Collections.Generic;
using System.Text;

namespace OrdenesOnline.Domain.DTO
{
    public class ValidatePasswordRequest
    {
        public string Correo { get; set; }
        public string Password { get; set; }
    }
}
