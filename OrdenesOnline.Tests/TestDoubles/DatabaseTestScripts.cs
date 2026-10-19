using Microsoft.Data.SqlClient;

namespace OrdenesOnline.Tests.TestDoubles;

internal static class DatabaseTestScripts
{
    public static Task<string> ReadCreate() => File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Database", "Create OrdenesOnline.Api.sql"));
    public static Task<string> ReadSeed() => File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Database", "Seed OrdenesOnline.Api.sql"));

    public static async Task InsertProposal(SqlConnection connection, int id)
    {
        await using var command = new SqlCommand("""
            SET IDENTITY_INSERT dbo.Propuesta ON;
            INSERT dbo.Propuesta (PropuestaId,Fecha_Registro,Nombre_Operador,Correo_Corporativo,Cosabcli,
                Tipo,Instrumento,Tipo_Orden,Cantidad,Vigencia,Mercado,estado)
            VALUES (@id,SYSUTCDATETIME(),'Prueba','prueba@example.com','C001','Compra','ABC','Limite',10,'Hoy','BVL','Pendiente');
            SET IDENTITY_INSERT dbo.Propuesta OFF;
            """, connection);
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync();
    }
}
