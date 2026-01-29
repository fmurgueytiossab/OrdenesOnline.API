using System;
using System.Collections.Generic;
using System.Text;

namespace OrdenesOnline.Domain.DTO
{
    public class UpdatePasswordByToken
    {
        public string Token { get; set; } = null!;
        public string Password { get; set; } = null!;
    }
}
