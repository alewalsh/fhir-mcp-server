namespace fhir_mcp_server.Mapping.Models;

public sealed class MedicationSummary
{
    public string? Name { get; init; }
    public string? Dose { get; init; }
    public string? Status { get; init; }
}
