using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace OrdenesOnline.Domain.entities
{
    public class Representante
    {
        [Key]
        public int RepresentanteId { get; set; }

        [Column("Correo_Corporativo")]
        public string CorreoCorporativo { get; set; } = null!;

        public byte[] Password { get; set; } = null!;
        public string Nombre { get; set; } = null!;
        public string Dni { get; set; } = null!;

    }
}
