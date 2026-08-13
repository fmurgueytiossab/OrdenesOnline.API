using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace OrdenesOnline.Domain.entities
{
    public class Valor
    {
        [Key]
        public string Cosabval { get; set; } = string.Empty;
        public string Mnemo { get; set; } = string.Empty;
        public string Comon { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public string Tival { get; set; } = string.Empty;
    }
}
