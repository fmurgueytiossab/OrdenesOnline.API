using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace OrdenesOnline.Domain.entities
{
    public class Propuesta
    {
        public int Id { get; set; }

        [Column("Nombre_Operador")]
        public string NombreOperador { get; set; } = null!;       
        [Column("Correo_Corporativo")]
        public string CorreoCorporativo { get; set; } = null!;
        public string Cosabcli { get; set; } = null!;
        public string Tipo { get; set; } = null!;
        public int Cantidad { get; set; }
        public string Instrumento { get; set; } = null!;
        public decimal Precio { get; set; }
        public string Moneda { get; set; } = null!;
    }
}
