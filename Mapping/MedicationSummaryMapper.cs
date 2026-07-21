using Hl7.Fhir.Model;
using fhir_mcp_server.Mapping.Models;

namespace fhir_mcp_server.Mapping;

public static class MedicationSummaryMapper
{
    public static MedicationSummary ToSummary(MedicationRequest request, Medication? includedMedication = null)
    {
        return new MedicationSummary
        {
            Name = ResolveName(request, includedMedication),
            Dose = FormatDose(request.DosageInstruction),
            Status = request.Status?.ToString()?.ToLowerInvariant()
        };
    }

    /// <summary>
    /// Collapse renewals into one summary line per drug display name.
    /// Business rule: not a raw FHIR field —
    /// status = active if any active MedicationRequest exists for that name,
    /// else the newest authoredOn overall.
    /// </summary>
    public static IReadOnlyList<MedicationSummary> ToDedupedSummaries(
        IEnumerable<(MedicationRequest Request, Medication? IncludedMedication)> items)
    {
        // ponytail: key = display name; distinct strengths stay distinct. Ceiling: O(n) on the capped page(s).
        return items
            .Select(t => (
                Summary: ToSummary(t.Request, t.IncludedMedication),
                Authored: ParseAuthored(t.Request.AuthoredOn),
                Active: t.Request.Status == MedicationRequest.MedicationrequestStatus.Active))
            .GroupBy(x => DrugKey(x.Summary.Name))
            .Select(PickPreferred)
            .ToList();
    }

    private static MedicationSummary PickPreferred(
        IGrouping<string, (MedicationSummary Summary, DateTimeOffset? Authored, bool Active)> group)
    {
        var actives = group.Where(x => x.Active).ToList();
        var pool = actives.Count > 0 ? actives : group.ToList();
        return pool
            .OrderByDescending(x => x.Authored ?? DateTimeOffset.MinValue)
            .First()
            .Summary;
    }

    private static string DrugKey(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "" : name.Trim().ToLowerInvariant();

    private static DateTimeOffset? ParseAuthored(string? authoredOn) =>
        ClinicalFormat.TryParseDate(authoredOn, out var dto) ? dto : null;

    private static string? ResolveName(MedicationRequest request, Medication? included)
    {
        if (request.Medication is CodeableConcept cc)
            return ConditionSummaryMapper.CodeableDisplay(cc);

        if (included?.Code is not null)
            return ConditionSummaryMapper.CodeableDisplay(included.Code);

        if (request.Medication is ResourceReference r)
            return string.IsNullOrWhiteSpace(r.Display) ? "Medication (unresolved reference)" : r.Display;

        return null;
    }

    private static string? FormatDose(List<Dosage>? instructions)
    {
        var d = instructions?.FirstOrDefault();
        if (d is null)
            return null;
        if (!string.IsNullOrWhiteSpace(d.Text))
            return d.Text;

        var rate = d.DoseAndRate?.FirstOrDefault();
        if (rate?.Dose is Quantity q)
            return ClinicalFormat.Quantity(q);

        return d.Timing?.Code?.Text
               ?? ConditionSummaryMapper.CodeableDisplay(d.Timing?.Code);
    }
}
