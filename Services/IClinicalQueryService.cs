namespace fhir_mcp_server.Services;

public interface IClinicalQueryService
{
    Task<string> SearchPatientsAsync(string name, CancellationToken ct = default);

    Task<string> GetPatientSummaryAsync(string patientId, CancellationToken ct = default);

    Task<string> GetConditionsAsync(string patientId, string? status = null, CancellationToken ct = default);

    Task<string> GetMedicationsAsync(string patientId, string? status = null, CancellationToken ct = default);

    Task<string> GetObservationsAsync(string patientId, string category, CancellationToken ct = default);
}
