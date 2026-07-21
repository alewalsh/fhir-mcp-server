using System.ComponentModel;
using fhir_mcp_server.Services;
using ModelContextProtocol.Server;

namespace fhir_mcp_server.Tools;

/// <summary>
/// MCP tool surface — thin orchestration over <see cref="IClinicalQueryService"/>.
/// Descriptions are the model-facing contract.
/// </summary>
[McpServerToolType]
public sealed class HealthcareTools(IClinicalQueryService clinical)
{
    private const string Disclaimer =
        " Synthetic Synthea data only — not a medical device and not for clinical use.";

    [McpServerTool(Name = "search_patients", ReadOnly = true, Destructive = false),
     Description(
         "Search for patients by full or partial name. Returns a list of matching patients with their FHIR ID, name, gender, and birth date. Use the returned ID with the other tools to look up clinical data — never identify a patient by name alone. Search by patient name only. This tool cannot search by condition, diagnosis, or clinical criteria."
         + Disclaimer)]
    public Task<string> SearchPatients(
        [Description("Full or partial patient name to search for.")] string name,
        CancellationToken cancellationToken = default)
        => clinical.SearchPatientsAsync(name, cancellationToken);

    [McpServerTool(Name = "get_patient_summary", ReadOnly = true, Destructive = false),
     Description(
         "Get a clinical summary for a patient by FHIR ID, including demographics, active conditions, and current medications. Use this as the primary overview of a patient."
         + Disclaimer)]
    public Task<string> GetPatientSummary(
        [Description("FHIR patient logical id (bare or Patient/{id}).")] string patient_id,
        CancellationToken cancellationToken = default)
        => clinical.GetPatientSummaryAsync(patient_id, cancellationToken);

    [McpServerTool(Name = "get_conditions", ReadOnly = true, Destructive = false),
     Description(
         "Get conditions for a patient by FHIR ID. Optionally filter by clinical status: active, resolved, or omit for all. Returns a summarized list (capped)."
         + Disclaimer)]
    public Task<string> GetConditions(
        [Description("FHIR patient logical id (bare or Patient/{id}).")] string patient_id,
        [Description("Optional clinical status filter: active, resolved. Omit for all.")] string? status = null,
        CancellationToken cancellationToken = default)
        => clinical.GetConditionsAsync(patient_id, status, cancellationToken);

    [McpServerTool(Name = "get_medications", ReadOnly = true, Destructive = false),
     Description(
         "Get medications for a patient by FHIR ID. Optionally filter by status: active, stopped, or omit for all. Returns deduplicated drug names with status (capped)."
         + Disclaimer)]
    public Task<string> GetMedications(
        [Description("FHIR patient logical id (bare or Patient/{id}).")] string patient_id,
        [Description("Optional MedicationRequest status filter: active, stopped. Omit for all.")] string? status = null,
        CancellationToken cancellationToken = default)
        => clinical.GetMedicationsAsync(patient_id, status, cancellationToken);

    [McpServerTool(Name = "get_observations", ReadOnly = true, Destructive = false),
     Description(
         "Get the latest observations for a patient by FHIR ID. Category is required: vital-signs or laboratory. Returns up to 20 newest results."
         + Disclaimer)]
    public Task<string> GetObservations(
        [Description("FHIR patient logical id (bare or Patient/{id}).")] string patient_id,
        [Description("Observation category: vital-signs or laboratory.")] string category,
        CancellationToken cancellationToken = default)
        => clinical.GetObservationsAsync(patient_id, category, cancellationToken);
}
