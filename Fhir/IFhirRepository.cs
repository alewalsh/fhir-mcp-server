using Hl7.Fhir.Model;

namespace fhir_mcp_server.Fhir;

// ponytail: single door for v1; split to IPatientRepository / etc. when method count or ownership hurts.
public interface IFhirRepository
{
    Task<Patient?> GetPatientAsync(string patientId, CancellationToken ct = default);

    Task<IReadOnlyList<Patient>> SearchPatientsByNameAsync(string name, int count = FhirQueryLimits.PatientSearch, CancellationToken ct = default);

    Task<IReadOnlyList<Condition>> SearchConditionsAsync(string patientId, string? clinicalStatus = null, int count = FhirQueryLimits.Conditions, CancellationToken ct = default);

    Task<IReadOnlyList<(MedicationRequest Request, Medication? IncludedMedication)>> SearchMedicationRequestsAsync(
        string patientId,
        string? status = null,
        int count = FhirQueryLimits.Medications,
        CancellationToken ct = default);

    Task<IReadOnlyList<Observation>> SearchObservationsAsync(
        string patientId,
        string category,
        int count = FhirQueryLimits.Observations,
        CancellationToken ct = default);
}
