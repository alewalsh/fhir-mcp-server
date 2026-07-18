namespace fhir_mcp_server.Mapping.Models;

public sealed class PatientSummary
{
    public required string Id { get; init; }
    public string? Name { get; init; }
    public string? Gender { get; init; }
    public string? BirthDate { get; init; }
    public int? AgeYears { get; init; }
}
