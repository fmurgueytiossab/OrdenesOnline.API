using System;
using System.Collections.Generic;
using System.Text;

namespace OrdenesOnline.Domain.DTO
{
    public class ZapierSendRequest
    {
        public int Id { get; set; }
        public string NombreOperador { get; set; }
        public string CorreoCorporativo { get; set; }
        public string Cosabcli { get; set; }
        public string Tipo { get; set; }
        public string TipoOrden { get; set; }
        public int Cantidad { get; set; }
        public string Instrumento { get; set; }
        public decimal? Precio { get; set; } = null;
        public string Mercado { get; set; }
        public string Moneda { get; set; }
        public string Dni { get; set; }
        public decimal? Monto { get; set; }
    }
}
