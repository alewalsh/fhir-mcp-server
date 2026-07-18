using System.Text;
using fhir_mcp_server.Mapping.Models;

namespace fhir_mcp_server.Mapping;

public static class SummaryFormatter
{
    public static string FormatPatientSummary(
        PatientSummary patient,
        IReadOnlyList<ConditionSummary> activeConditions,
        IReadOnlyList<MedicationSummary> currentMedications)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Demographics");
        sb.AppendLine($"- ID: {patient.Id}");
        sb.AppendLine($"- Name: {patient.Name ?? "unknown"}");
        sb.AppendLine($"- Gender: {patient.Gender ?? "unknown"}");
        sb.AppendLine($"- Birth date: {patient.BirthDate ?? "unknown"}");
        sb.AppendLine($"- Age: {(patient.AgeYears is int a ? $"{ClinicalFormat.Int(a)} years" : "unknown")}");
        sb.AppendLine();
        sb.AppendLine("Active conditions");
        AppendConditionBullets(sb, activeConditions, emptyLabel: "None");
        sb.AppendLine();
        sb.AppendLine("Current medications");
        AppendMedicationBullets(sb, currentMedications, emptyLabel: "None");
        return sb.ToString().TrimEnd();
    }

    public static string FormatConditionList(
        string patientId,
        IReadOnlyList<ConditionSummary> conditions,
        bool truncated = false)
    {
        if (conditions.Count == 0)
            return $"No conditions found for patient {patientId}.";

        var sb = new StringBuilder();
        sb.AppendLine($"Conditions for patient {patientId}{TruncationSuffix(truncated, conditions.Count)}:");
        AppendConditionBullets(sb, conditions, emptyLabel: null);
        return sb.ToString().TrimEnd();
    }

    public static string FormatMedicationList(
        string patientId,
        IReadOnlyList<MedicationSummary> medications,
        bool truncated = false)
    {
        if (medications.Count == 0)
            return $"No medications found for patient {patientId}.";

        var sb = new StringBuilder();
        sb.AppendLine($"Medications for patient {patientId}{TruncationSuffix(truncated, medications.Count)}:");
        AppendMedicationBullets(sb, medications, emptyLabel: null);
        return sb.ToString().TrimEnd();
    }

    public static string FormatObservationList(
        string patientId,
        string category,
        IReadOnlyList<ObservationSummary> observations,
        bool truncated = false)
    {
        if (observations.Count == 0)
            return $"No observations found for patient {patientId}.";

        var sb = new StringBuilder();
        sb.AppendLine($"Observations ({category}) for patient {patientId}{TruncationSuffix(truncated, observations.Count)}:");
        foreach (var o in observations)
        {
            var display = o.Display ?? "unknown";
            var value = o.Value ?? "no value";
            var date = o.EffectiveDate ?? "unknown date";
            sb.AppendLine($"- {display}: {value} ({date})");
        }

        return sb.ToString().TrimEnd();
    }

    public static string FormatPatientSearch(string name, IReadOnlyList<PatientSummary> patients, int cap)
    {
        // Runtime defense: the model learns limits better from results than from tool descriptions
        // (Checkpoint E: description wording alone did not stop condition-as-name probes).
        if (patients.Count == 0)
            return $"No patients found with a name matching \"{name}\". This parameter matches patient names only. "
                 + "This server cannot find patients by condition, diagnosis, or any clinical criteria — there is no such capability. "
                 + "Do not retry with other clinical terms.";

        var truncated = patients.Count >= cap;
        var sb = new StringBuilder();
        sb.AppendLine($"Matching patients{TruncationSuffix(truncated, patients.Count)}:");
        foreach (var p in patients)
        {
            sb.AppendLine(
                $"- id={p.Id}; name={p.Name ?? "unknown"}; gender={p.Gender ?? "unknown"}; birthDate={p.BirthDate ?? "unknown"}");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>Shared cap copy: "showing N of more…" when the FHIR page hit its limit.</summary>
    public static string TruncationSuffix(bool truncated, int shown) =>
        truncated ? $" (showing {ClinicalFormat.Int(shown)} of more…)" : "";

    private static void AppendConditionBullets(StringBuilder sb, IReadOnlyList<ConditionSummary> conditions, string? emptyLabel)
    {
        if (conditions.Count == 0)
        {
            if (emptyLabel is not null)
                sb.AppendLine($"- {emptyLabel}");
            return;
        }

        foreach (var c in conditions)
        {
            var display = c.Display ?? "unknown condition";
            var status = c.Status is null ? "" : $" [{c.Status}]";
            var onset = c.OnsetDate is null ? "" : $" (onset {c.OnsetDate})";
            sb.AppendLine($"- {display}{status}{onset}");
        }
    }

    private static void AppendMedicationBullets(StringBuilder sb, IReadOnlyList<MedicationSummary> medications, string? emptyLabel)
    {
        if (medications.Count == 0)
        {
            if (emptyLabel is not null)
                sb.AppendLine($"- {emptyLabel}");
            return;
        }

        foreach (var m in medications)
        {
            var name = m.Name ?? "unknown medication";
            var status = m.Status ?? "unknown";
            var dose = m.Dose is null ? "" : $"; dose: {m.Dose}";
            // Clinically readable: one drug line with latest status.
            sb.AppendLine($"- {name} — {status}{dose}");
        }
    }
}
