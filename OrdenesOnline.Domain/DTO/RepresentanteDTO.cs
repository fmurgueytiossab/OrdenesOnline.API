using System;
using System.Collections.Generic;
using System.Text;

namespace OrdenesOnline.Domain.DTO
{
    public class RepresentanteDTO
    {
        public int RepresentanteId { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string CorreoCorporativo { get; set; } = string.Empty;
        public string Dni { get; set; } = string.Empty;
        public List<string> Cosabcli { get; set; } = new();
    }
}
