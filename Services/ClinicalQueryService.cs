using System.Diagnostics;
using fhir_mcp_server.Fhir;
using fhir_mcp_server.Mapping;
using Microsoft.Extensions.Logging;

namespace fhir_mcp_server.Services;

public sealed class ClinicalQueryService : IClinicalQueryService
{
    private readonly IFhirRepository _fhir;
    private readonly ILogger<ClinicalQueryService> _logger;

    public ClinicalQueryService(IFhirRepository fhir, ILogger<ClinicalQueryService> logger)
    {
        _fhir = fhir;
        _logger = logger;
    }

    public async Task<string> SearchPatientsAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Invalid name: name is required.";

        var sw = Stopwatch.StartNew();
        try
        {
            var patients = await _fhir.SearchPatientsByNameAsync(name.Trim(), FhirQueryLimits.PatientSearch, ct);
            var summaries = patients.Select(PatientSummaryMapper.ToSummary).ToList();
            _logger.LogInformation(
                "tool={Tool} result_count={ResultCount} duration_ms={DurationMs}",
                "search_patients", summaries.Count, sw.ElapsedMilliseconds);
            return SummaryFormatter.FormatPatientSearch(name.Trim(), summaries, FhirQueryLimits.PatientSearch);
        }
        catch (FhirQueryException ex)
        {
            return $"Failed to query FHIR server: {ex.Message}";
        }
    }

    public async Task<string> GetPatientSummaryAsync(string patientId, CancellationToken ct = default)
    {
        var id = PatientId.Normalize(patientId);
        if (id is null)
            return "Invalid patient id: patient id is required.";

        var sw = Stopwatch.StartNew();
        try
        {
            var patient = await _fhir.GetPatientAsync(id, ct);
            if (patient is null)
                return $"Patient {id} not found.";

            var conditionsTask = _fhir.SearchConditionsAsync(id, clinicalStatus: "active", ct: ct);
            var medsTask = _fhir.SearchMedicationRequestsAsync(id, status: "active", ct: ct);
            await Task.WhenAll(conditionsTask, medsTask);

            var conditions = (await conditionsTask).Select(ConditionSummaryMapper.ToSummary).ToList();
            var meds = MedicationSummaryMapper.ToDedupedSummaries(await medsTask);

            var summary = PatientSummaryMapper.ToSummary(patient);
            _logger.LogInformation(
                "tool={Tool} patient_id={PatientId} result_count={ResultCount} duration_ms={DurationMs}",
                "get_patient_summary", id, conditions.Count + meds.Count, sw.ElapsedMilliseconds);

            return SummaryFormatter.FormatPatientSummary(summary, conditions, meds);
        }
        catch (FhirQueryException ex)
        {
            return $"Failed to query FHIR server: {ex.Message}";
        }
    }

    public async Task<string> GetConditionsAsync(string patientId, string? status = null, CancellationToken ct = default)
    {
        var id = PatientId.Normalize(patientId);
        if (id is null)
            return "Invalid patient id: patient id is required.";

        if (status is not null && status is not ("active" or "resolved"))
            return $"Invalid status: '{status}'. Allowed: active, resolved (or omit for all).";

        var sw = Stopwatch.StartNew();
        try
        {
            if (await _fhir.GetPatientAsync(id, ct) is null)
                return $"Patient {id} not found.";

            var conditions = await _fhir.SearchConditionsAsync(id, clinicalStatus: status, ct: ct);
            var truncated = conditions.Count >= FhirQueryLimits.Conditions;
            var summaries = conditions.Select(ConditionSummaryMapper.ToSummary).ToList();
            _logger.LogInformation(
                "tool={Tool} patient_id={PatientId} result_count={ResultCount} truncated={Truncated} duration_ms={DurationMs}",
                "get_conditions", id, summaries.Count, truncated, sw.ElapsedMilliseconds);
            return SummaryFormatter.FormatConditionList(id, summaries, truncated);
        }
        catch (FhirQueryException ex)
        {
            return $"Failed to query FHIR server: {ex.Message}";
        }
    }

    public async Task<string> GetMedicationsAsync(string patientId, string? status = null, CancellationToken ct = default)
    {
        var id = PatientId.Normalize(patientId);
        if (id is null)
            return "Invalid patient id: patient id is required.";

        if (status is not null && status is not ("active" or "stopped"))
            return $"Invalid status: '{status}'. Allowed: active, stopped (or omit for all).";

        var sw = Stopwatch.StartNew();
        try
        {
            if (await _fhir.GetPatientAsync(id, ct) is null)
                return $"Patient {id} not found.";

            IReadOnlyList<(Hl7.Fhir.Model.MedicationRequest Request, Hl7.Fhir.Model.Medication? IncludedMedication)> meds;
            bool truncated;
            if (status is null)
            {
                // Include actives explicitly — an unfiltered _count page is dominated by stopped renewals.
                var activeTask = _fhir.SearchMedicationRequestsAsync(id, status: "active", ct: ct);
                var stoppedTask = _fhir.SearchMedicationRequestsAsync(id, status: "stopped", ct: ct);
                await Task.WhenAll(activeTask, stoppedTask);
                var active = await activeTask;
                var stopped = await stoppedTask;
                truncated = active.Count >= FhirQueryLimits.Medications || stopped.Count >= FhirQueryLimits.Medications;
                meds = active.Concat(stopped).ToList();
            }
            else
            {
                meds = await _fhir.SearchMedicationRequestsAsync(id, status: status, ct: ct);
                truncated = meds.Count >= FhirQueryLimits.Medications;
            }

            var summaries = MedicationSummaryMapper.ToDedupedSummaries(meds);
            _logger.LogInformation(
                "tool={Tool} patient_id={PatientId} result_count={ResultCount} truncated={Truncated} duration_ms={DurationMs}",
                "get_medications", id, summaries.Count, truncated, sw.ElapsedMilliseconds);
            return SummaryFormatter.FormatMedicationList(id, summaries, truncated);
        }
        catch (FhirQueryException ex)
        {
            return $"Failed to query FHIR server: {ex.Message}";
        }
    }

    public async Task<string> GetObservationsAsync(string patientId, string category, CancellationToken ct = default)
    {
        var id = PatientId.Normalize(patientId);
        if (id is null)
            return "Invalid patient id: patient id is required.";

        if (category is not ("vital-signs" or "laboratory"))
            return $"Invalid category: '{category}'. Allowed: vital-signs, laboratory.";

        var sw = Stopwatch.StartNew();
        try
        {
            if (await _fhir.GetPatientAsync(id, ct) is null)
                return $"Patient {id} not found.";

            var observations = await _fhir.SearchObservationsAsync(id, category, FhirQueryLimits.Observations, ct);
            var truncated = observations.Count >= FhirQueryLimits.Observations;
            var summaries = observations.Select(ObservationSummaryMapper.ToSummary).ToList();
            _logger.LogInformation(
                "tool={Tool} patient_id={PatientId} result_count={ResultCount} truncated={Truncated} duration_ms={DurationMs}",
                "get_observations", id, summaries.Count, truncated, sw.ElapsedMilliseconds);
            return SummaryFormatter.FormatObservationList(id, category, summaries, truncated);
        }
        catch (FhirQueryException ex)
        {
            return $"Failed to query FHIR server: {ex.Message}";
        }
    }
}
