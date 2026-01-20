namespace OrdenesOnline_API.Models
{
    public class PropuestaCreateRequest
    {
        public string NombreOperador { get; set; } = null!;       
        public string CorreoCorporativo { get; set; } = null!;
        public string Cosabcli { get; set; } = null!;
        public string Tipo { get; set; } = null!;
        public int Cantidad { get; set; }
        public string Instrumento { get; set; } = null!;
        public decimal Precio { get; set; }
        public string Moneda { get; set; } = null!;
    }
}
