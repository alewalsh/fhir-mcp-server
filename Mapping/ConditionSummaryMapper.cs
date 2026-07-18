using Hl7.Fhir.Model;
using fhir_mcp_server.Mapping.Models;

namespace fhir_mcp_server.Mapping;

public static class ConditionSummaryMapper
{
    public static ConditionSummary ToSummary(Condition condition) => new()
    {
        Display = CodeableDisplay(condition.Code),
        Status = CodeableCode(condition.ClinicalStatus),
        OnsetDate = FormatOnset(condition.Onset)
    };

    private static string? FormatOnset(DataType? onset) => onset switch
    {
        FhirDateTime dt => ClinicalFormat.Date(dt.Value),
        Period p => ClinicalFormat.Date(p.Start) ?? ClinicalFormat.Date(p.End),
        Age age => ClinicalFormat.Quantity(age.Value, age.Unit),
        _ => onset?.ToString()
    };

    internal static string? CodeableDisplay(CodeableConcept? cc)
    {
        if (cc is null)
            return null;
        if (!string.IsNullOrWhiteSpace(cc.Text))
            return cc.Text;
        return cc.Coding?.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Display))?.Display
               ?? cc.Coding?.FirstOrDefault()?.Code;
    }

    internal static string? CodeableCode(CodeableConcept? cc) =>
        cc?.Coding?.FirstOrDefault()?.Code ?? cc?.Text;
}
