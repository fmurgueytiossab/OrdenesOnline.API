using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace OrdenesOnline.Domain.entities
{
    public class CodeRepresentante
    {
        public int RepresentanteId { get; set; }
        public string Cosabcli { get; set; }
    }
}
