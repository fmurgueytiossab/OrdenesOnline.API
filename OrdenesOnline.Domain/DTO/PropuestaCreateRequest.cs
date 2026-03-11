namespace OrdenesOnline.Domain.DTO
{
    public class PropuestaCreateRequest
    {
        public string NombreOperador { get; set; }
        public string CorreoCorporativo { get; set; }
        public string Cosabcli { get; set; }
        public string Tipo { get; set; }
        public string TipoOrden { get; set; }
        public int Cantidad { get; set; }
        public string Instrumento { get; set; }
        public decimal? Precio { get; set; } = null;
        public decimal? Monto { get; set; }
        public string Mercado { get; set; }
        public string Moneda { get; set; }
        public string Dni { get; set; }
        public string Vigencia { get; set; }
        public DateTime FechaRegistro  { get; set; }
    }
}
