using System.ComponentModel.DataAnnotations.Schema;

namespace OrdenesOnline_API.Models
{
    public class RepresentanteCreateRequest
    {       
        public string CorreoCorporativo { get; set; } = null!;

        public byte[] Password { get; set; } = null!;
        public string Nombre { get; set; } = null!;
        public string Cosabcli { get; set; } = null!;
    }
}
