using Hl7.Fhir.Model;
using fhir_mcp_server.Mapping.Models;

namespace fhir_mcp_server.Mapping;

public static class ObservationSummaryMapper
{
    public static ObservationSummary ToSummary(Observation observation) => new()
    {
        Display = ConditionSummaryMapper.CodeableDisplay(observation.Code),
        Value = FormatValue(observation),
        EffectiveDate = FormatEffective(observation.Effective),
        Category = observation.Category?.Select(ConditionSummaryMapper.CodeableDisplay)
            .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))
    };

    private static string? FormatValue(Observation o)
    {
        if (o.Value is Quantity q)
            return ClinicalFormat.Quantity(q);
        if (o.Value is CodeableConcept cc)
            return ConditionSummaryMapper.CodeableDisplay(cc);
        if (o.Value is FhirString s)
            return s.Value;
        if (o.Value is FhirBoolean b)
            return b.Value?.ToString();
        if (o.Value is Integer i)
            return i.Value is null ? null : ClinicalFormat.Int(i.Value.Value);
        if (o.Value is FhirDecimal dec)
            return dec.Value is null ? null : ClinicalFormat.Decimal(dec.Value.Value);

        if (o.Component is { Count: > 0 })
        {
            var parts = o.Component
                .Select(c =>
                {
                    var name = ConditionSummaryMapper.CodeableDisplay(c.Code) ?? "component";
                    var val = c.Value switch
                    {
                        Quantity q => ClinicalFormat.Quantity(q),
                        CodeableConcept cc => ConditionSummaryMapper.CodeableDisplay(cc),
                        FhirString s => s.Value,
                        Integer i => i.Value is null ? null : ClinicalFormat.Int(i.Value.Value),
                        FhirDecimal d => d.Value is null ? null : ClinicalFormat.Decimal(d.Value.Value),
                        _ => c.Value?.ToString()
                    };
                    return val is null ? name : $"{name}: {val}";
                })
                .Where(p => !string.IsNullOrWhiteSpace(p));
            var joined = string.Join("; ", parts);
            return string.IsNullOrWhiteSpace(joined) ? null : joined;
        }

        return o.Value?.ToString();
    }

    private static string? FormatEffective(DataType? effective) => effective switch
    {
        FhirDateTime dt => ClinicalFormat.Date(dt.Value),
        Period p => ClinicalFormat.Date(p.Start) ?? ClinicalFormat.Date(p.End),
        _ => effective?.ToString()
    };
}
