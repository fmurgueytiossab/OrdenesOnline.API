using System;
using System.Collections.Generic;
using System.Text;

namespace OrdenesOnline.Domain.DTO
{
    public class RepresentanteDTO
    {
        public int RepresentanteId { get; set; }
        public string Nombre { get; set; }
        public string CorreoCorporativo { get; set; }
        public string Dni { get; set; }
        public List<string> Cosabcli { get; set; } = new();
    }
}
