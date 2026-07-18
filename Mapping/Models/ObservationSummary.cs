namespace fhir_mcp_server.Mapping.Models;

public sealed class ObservationSummary
{
    public string? Display { get; init; }
    public string? Value { get; init; }
    public string? EffectiveDate { get; init; }
    public string? Category { get; init; }
}
