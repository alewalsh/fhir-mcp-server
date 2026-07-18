using Hl7.Fhir.Model;
using fhir_mcp_server.Mapping.Models;

namespace fhir_mcp_server.Mapping;

public static class PatientSummaryMapper
{
    public static PatientSummary ToSummary(Patient patient)
    {
        var birthDate = ClinicalFormat.Date(patient.BirthDate) ?? patient.BirthDate;
        return new PatientSummary
        {
            Id = patient.Id ?? "",
            Name = FormatName(patient.Name),
            Gender = patient.Gender?.ToString()?.ToLowerInvariant(),
            BirthDate = birthDate,
            AgeYears = AgeFromBirthDate(patient.BirthDate)
        };
    }

    private static string? FormatName(List<HumanName>? names)
    {
        var name = names?.FirstOrDefault(n => n.Use == HumanName.NameUse.Official)
                   ?? names?.FirstOrDefault();
        if (name is null)
            return null;

        var givenParts = name.Given?.Where(g => !string.IsNullOrWhiteSpace(g)).ToList();
        var given = givenParts is { Count: > 0 } ? string.Join(" ", givenParts) : null;
        var family = name.Family;
        if (given is null && family is null)
            return name.Text;

        if (given is null)
            return family;
        if (family is null)
            return given;
        return $"{given} {family}";
    }

    private static int? AgeFromBirthDate(string? birthDate)
    {
        if (!ClinicalFormat.TryParseDateTime(birthDate, out var dob))
            return null;

        var today = DateTime.UtcNow.Date;
        var age = today.Year - dob.Year;
        if (dob.Date > today.AddYears(-age))
            age--;
        return age < 0 ? null : age;
    }
}
