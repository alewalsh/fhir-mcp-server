using System.Globalization;
using Hl7.Fhir.Model;

namespace fhir_mcp_server.Mapping;

/// <summary>
/// Clinical summaries always use invariant number/date formatting (international context).
/// </summary>
public static class ClinicalFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Decimal(decimal value) => value.ToString(Inv);

    public static string Decimal(decimal? value) =>
        value is null ? "" : value.Value.ToString(Inv);

    public static string Int(int value) => value.ToString(Inv);

    public static string? Quantity(Quantity? q)
    {
        if (q?.Value is null)
            return string.IsNullOrWhiteSpace(q?.Unit) ? null : q!.Unit!.Trim();

        var n = Decimal(q.Value.Value);
        return string.IsNullOrWhiteSpace(q.Unit) ? n : $"{n} {q.Unit}".Trim();
    }

    public static string? Quantity(decimal? value, string? unit)
    {
        if (value is null)
            return string.IsNullOrWhiteSpace(unit) ? null : unit.Trim();
        var n = Decimal(value.Value);
        return string.IsNullOrWhiteSpace(unit) ? n : $"{n} {unit}".Trim();
    }

    public static string? Date(string? fhirInstantOrDate)
    {
        if (string.IsNullOrWhiteSpace(fhirInstantOrDate))
            return null;

        if (!TryParseDate(fhirInstantOrDate, out var dto))
            return fhirInstantOrDate.Trim();

        // Date-only FHIR values stay yyyy-MM-dd; instants stay ISO-8601 with offset (invariant).
        if (fhirInstantOrDate.Trim().Length <= 10)
            return dto.ToString("yyyy-MM-dd", Inv);

        return dto.ToString("yyyy-MM-dd'T'HH:mm:sszzz", Inv);
    }

    public static bool TryParseDate(string? raw, out DateTimeOffset dto)
    {
        dto = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        return DateTimeOffset.TryParse(raw, Inv, DateTimeStyles.RoundtripKind, out dto)
               || DateTimeOffset.TryParse(raw, Inv, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dto);
    }

    public static bool TryParseDateTime(string? raw, out DateTime date)
    {
        date = default;
        if (!TryParseDate(raw, out var dto))
            return false;
        date = dto.Date;
        return true;
    }
}
