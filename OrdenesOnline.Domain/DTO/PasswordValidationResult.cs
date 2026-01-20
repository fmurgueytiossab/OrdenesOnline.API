using System;
using System.Collections.Generic;
using System.Text;

namespace OrdenesOnline.Domain.DTO
{
    public class PasswordValidationResult
    {
        public int IsValid { get; set; }
        public int UserId { get; set; }
    }
}
