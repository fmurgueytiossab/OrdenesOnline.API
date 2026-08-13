using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace OrdenesOnline.Domain.entities
{
    public class Propuesta
    {
        public int PropuestaId { get; set; }

        [Column("Nombre_Operador")]
        public string NombreOperador { get; set; } = null!;
        [Column("Correo_Corporativo")]
        public string CorreoCorporativo { get; set; } = null!;
        public string Cosabcli { get; set; } = null!;
        public string Tipo { get; set; } = null!;
        [Column("Tipo_Orden")]
        public string TipoOrden { get; set; } = null!;
        public int Cantidad { get; set; }
        public decimal? Monto { get; set; } = null!;
        public string Instrumento { get; set; } = null!;
        public decimal? Precio { get; set; }
        public string Vigencia { get; set; } = null!;
        public string Mercado { get; set; } = null!;
        [Column("Fecha_Registro")]
        public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;

    }
}
