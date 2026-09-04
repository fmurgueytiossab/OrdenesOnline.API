using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;
using OrdenesOnline.Infrastructure.Persistence;

namespace OrdenesOnline.Infrastructure.Repositories;

public sealed class PropuestaBvlSeguimientoRepository : IPropuestaBvlSeguimientoRepository
{
    private const int ClientCodeBatchSize = 500;
    private readonly AppDbContext _appContext;
    private readonly OpersabDbContext _opersabContext;
    private readonly IRepresentanteClientScopeRepository _clientScopeRepository;

    public PropuestaBvlSeguimientoRepository(
        AppDbContext appContext,
        OpersabDbContext opersabContext,
        IRepresentanteClientScopeRepository clientScopeRepository)
    {
        _appContext = appContext;
        _opersabContext = opersabContext;
        _clientScopeRepository = clientScopeRepository;
    }

    public async Task<PropuestaBvlSeguimientoSnapshot> GetAsync(
        int representanteId,
        CancellationToken cancellationToken = default)
    {
        var clientScope = await _clientScopeRepository.GetAsync(
            representanteId,
            cancellationToken);

        if (!clientScope.RepresentanteExiste)
        {
            return new PropuestaBvlSeguimientoSnapshot(false, [], []);
        }

        if (clientScope.Cosabcli.Count == 0)
        {
            return new PropuestaBvlSeguimientoSnapshot(true, [], []);
        }

        var fechaInicio = DateTime.Today;
        var fechaFin = fechaInicio.AddDays(1);
        var propuestas = new Dictionary<int, Propuesta>();
        foreach (var clientCodeBatch in clientScope.Cosabcli.Chunk(ClientCodeBatchSize))
        {
            var batchPropuestas = await _appContext.Propuestas
                .AsNoTracking()
                .Where(propuesta =>
                    propuesta.Mercado == "BVL" &&
                    propuesta.FechaRegistro >= fechaInicio &&
                    propuesta.FechaRegistro < fechaFin &&
                    clientCodeBatch.Contains(propuesta.Cosabcli))
                .ToListAsync(cancellationToken);

            foreach (var propuesta in batchPropuestas)
            {
                propuestas[propuesta.PropuestaId] = propuesta;
            }
        }

        var operaciones = await GetOperacionesAsync(
            clientScope.Cosabcli,
            DateOnly.FromDateTime(fechaInicio),
            cancellationToken);

        return new PropuestaBvlSeguimientoSnapshot(
            true,
            propuestas.Values.ToList(),
            operaciones);
    }

    private async Task<IReadOnlyList<OperacionBvl>> GetOperacionesAsync(
        IReadOnlyList<string> clientCodes,
        DateOnly fecha,
        CancellationToken cancellationToken)
    {
        var result = new List<OperacionBvl>();
        var connection = _opersabContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            foreach (var clientCodeBatch in clientCodes.Chunk(ClientCodeBatchSize))
            {
                await using var command = connection.CreateCommand();
                var parameterNames = new List<string>(clientCodeBatch.Length);

                for (var index = 0; index < clientCodeBatch.Length; index++)
                {
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = $"@cosabcli{index}";
                    parameter.DbType = DbType.String;
                    parameter.Value = clientCodeBatch[index];
                    command.Parameters.Add(parameter);
                    parameterNames.Add(parameter.ParameterName);
                }

                var fechaParameter = command.CreateParameter();
                fechaParameter.ParameterName = "@fecha";
                fechaParameter.DbType = DbType.Date;
                fechaParameter.Value = fecha.ToDateTime(TimeOnly.MinValue);
                command.Parameters.Add(fechaParameter);

                command.CommandText = $$"""
                    SELECT DISTINCT
                        rueda.cosabcli AS Cosabcli,
                        rueda.fchprop AS FechaPropuesta,
                        rueda.horaprop AS HoraPropuesta,
                        rueda.nuprop AS NumeroPropuesta,
                        rueda.mnemo AS Instrumento,
                        rueda.qt_ejec AS CantidadEjecutada,
                        rueda.qt_anul AS CantidadAnulada,
                        rueda.fg_cv AS Tipo,
                        rueda.qt_prop AS CantidadPropuesta,
                        rueda.prprop AS Precio
                    FROM elex_rueda_2_tmp AS rueda
                    LEFT OUTER JOIN clientes
                        ON rueda.cosabcli = clientes.cosabcli
                    LEFT JOIN sab_operadores
                        ON sab_operadores.codper = clientes.codres
                    WHERE rueda.cia = '019'
                      AND rueda.tipo_reg = 'P'
                      AND rueda.cosabcli IN ({{string.Join(", ", parameterNames)}})
                      AND rueda.fchprop = @fecha
                      AND (
                          sab_operadores.fchfin IS NULL OR
                          sab_operadores.fchfin > GETDATE()
                      );
                    """;

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    result.Add(ReadOperacion(reader));
                }
            }
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }

        return result;
    }

    private static OperacionBvl ReadOperacion(DbDataReader reader) => new(
        ReadString(reader, "Cosabcli"),
        ReadDate(reader, "FechaPropuesta"),
        ReadTime(reader, "HoraPropuesta"),
        ReadString(reader, "NumeroPropuesta"),
        ReadString(reader, "Instrumento"),
        ReadDecimal(reader, "CantidadEjecutada"),
        ReadDecimal(reader, "CantidadAnulada"),
        ReadString(reader, "Tipo"),
        ReadDecimal(reader, "CantidadPropuesta"),
        ReadNullableDecimal(reader, "Precio"));

    private static object? ReadValue(DbDataReader reader, string columnName)
    {
        var value = reader.GetValue(reader.GetOrdinal(columnName));
        return value == DBNull.Value ? null : value;
    }

    private static string ReadString(DbDataReader reader, string columnName) =>
        Convert.ToString(ReadValue(reader, columnName), CultureInfo.InvariantCulture)?.Trim()
        ?? string.Empty;

    private static decimal ReadDecimal(DbDataReader reader, string columnName)
    {
        var value = ReadValue(reader, columnName);
        return value is null ? 0m : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
    }

    private static decimal? ReadNullableDecimal(DbDataReader reader, string columnName)
    {
        var value = ReadValue(reader, columnName);
        return value is null ? null : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
    }

    private static DateOnly ReadDate(DbDataReader reader, string columnName)
    {
        var value = ReadValue(reader, columnName)
            ?? throw new DataException($"La columna '{columnName}' no puede ser nula.");

        if (value is DateOnly date)
        {
            return date;
        }

        if (value is DateTime dateTime)
        {
            return DateOnly.FromDateTime(dateTime);
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        string[] formats = ["yyyyMMdd", "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy"];
        if (DateOnly.TryParseExact(
                text,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return parsed;
        }

        throw new DataException($"El valor '{text}' de la columna '{columnName}' no es una fecha válida.");
    }

    private static TimeOnly? ReadTime(DbDataReader reader, string columnName)
    {
        var value = ReadValue(reader, columnName);
        if (value is null)
        {
            return null;
        }

        if (value is TimeOnly timeOnly)
        {
            return timeOnly;
        }

        if (value is TimeSpan timeSpan)
        {
            return TimeOnly.FromTimeSpan(timeSpan);
        }

        if (value is DateTime dateTime)
        {
            return TimeOnly.FromDateTime(dateTime);
        }

        var text = value is byte or sbyte or short or ushort or int or uint or long or ulong or
            float or double or decimal
            ? decimal.Truncate(Convert.ToDecimal(value, CultureInfo.InvariantCulture))
                .ToString("0", CultureInfo.InvariantCulture)
            : Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        if (TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }

        var digits = new string((text ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length is > 0 and <= 6 &&
            TimeOnly.TryParseExact(
                digits.PadLeft(6, '0'),
                "HHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out parsed))
        {
            return parsed;
        }

        throw new DataException($"El valor '{text}' de la columna '{columnName}' no es una hora válida.");
    }
}
