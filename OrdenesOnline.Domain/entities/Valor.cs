using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace OrdenesOnline.Domain.entities
{
    public class Valor
    {
        [Key]
        public string Cosabval { get; set; }
        public string Mnemo { get; set; }
        public string Comon { get; set; }
        public string Estado { get; set; }
    }
}
