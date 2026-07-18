using System.Diagnostics;
using System.Net;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace fhir_mcp_server.Fhir;

public sealed class FirelyFhirRepository : IFhirRepository, IDisposable
{
    private readonly FhirClient _client;
    private readonly ILogger<FirelyFhirRepository> _logger;

    public FirelyFhirRepository(IOptions<FhirOptions> options, ILogger<FirelyFhirRepository> logger)
    {
        _logger = logger;
        var baseUrl = options.Value.BaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Fhir:BaseUrl is required (set via appsettings or env Fhir__BaseUrl).");

        _client = new FhirClient(baseUrl, new FhirClientSettings
        {
            PreferredFormat = ResourceFormat.Json,
            VerifyFhirVersion = false
        });
    }

    public async Task<Patient?> GetPatientAsync(string patientId, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var patient = await _client.ReadAsync<Patient>(new Uri($"Patient/{patientId}", UriKind.Relative));
            Log("GetPatient", patientId, patient is null ? 0 : 1, sw);
            return patient;
        }
        catch (FhirOperationException ex) when (ex.Status == HttpStatusCode.NotFound)
        {
            Log("GetPatient", patientId, 0, sw);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Wrap("GetPatient", patientId, ex);
        }
    }

    public async Task<IReadOnlyList<Patient>> SearchPatientsByNameAsync(string name, int count = FhirQueryLimits.PatientSearch, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var q = new SearchParams()
                .Where($"name={name}")
                .LimitTo(count);
            var bundle = await _client.SearchAsync<Patient>(q);
            var list = Matches<Patient>(bundle);
            Log("SearchPatientsByName", name, list.Count, sw);
            return list;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Wrap("SearchPatientsByName", name, ex);
        }
    }

    public async Task<IReadOnlyList<Condition>> SearchConditionsAsync(
        string patientId,
        string? clinicalStatus = null,
        int count = FhirQueryLimits.Conditions,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var q = new SearchParams()
                .Where($"patient={patientId}")
                .LimitTo(count);
            if (!string.IsNullOrWhiteSpace(clinicalStatus))
                q.Where($"clinical-status={clinicalStatus}");

            var bundle = await _client.SearchAsync<Condition>(q);
            var list = Matches<Condition>(bundle);
            Log("SearchConditions", patientId, list.Count, sw);
            return list;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Wrap("SearchConditions", patientId, ex);
        }
    }

    public async Task<IReadOnlyList<(MedicationRequest Request, Medication? IncludedMedication)>> SearchMedicationRequestsAsync(
        string patientId,
        string? status = null,
        int count = FhirQueryLimits.Medications,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var q = new SearchParams()
                .Where($"patient={patientId}")
                .LimitTo(count)
                .Include("MedicationRequest:medication");
            if (!string.IsNullOrWhiteSpace(status))
                q.Where($"status={status}");

            var bundle = await _client.SearchAsync<MedicationRequest>(q);
            var medsById = IndexIncludedMedications(bundle);
            var requests = Matches<MedicationRequest>(bundle);
            var result = new List<(MedicationRequest, Medication?)>(requests.Count);

            foreach (var mr in requests)
            {
                Medication? included = null;
                if (mr.Medication is ResourceReference medRef && !string.IsNullOrEmpty(medRef.Reference))
                {
                    var id = LogicalId(medRef.Reference);
                    if (id is not null)
                        medsById.TryGetValue(id, out included);
                }

                // ponytail: N reads if _include empty; ceiling = one Read per distinct medication id on the page.
                if (included is null && mr.Medication is ResourceReference unresolved && !string.IsNullOrEmpty(unresolved.Reference))
                {
                    var id = LogicalId(unresolved.Reference);
                    if (id is not null && !medsById.ContainsKey(id))
                    {
                        included = await TryReadMedicationAsync(id, ct);
                        if (included is not null)
                            medsById[id] = included;
                    }
                    else if (id is not null)
                        medsById.TryGetValue(id, out included);
                }

                result.Add((mr, included));
            }

            Log("SearchMedicationRequests", patientId, result.Count, sw);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Wrap("SearchMedicationRequests", patientId, ex);
        }
    }

    public async Task<IReadOnlyList<Observation>> SearchObservationsAsync(
        string patientId,
        string category,
        int count = FhirQueryLimits.Observations,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var q = new SearchParams()
                .Where($"patient={patientId}")
                .Where($"category={category}")
                .LimitTo(count)
                .OrderBy("date", SortOrder.Descending);

            var bundle = await _client.SearchAsync<Observation>(q);
            var list = Matches<Observation>(bundle);
            Log("SearchObservations", patientId, list.Count, sw);
            return list;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Wrap("SearchObservations", patientId, ex);
        }
    }

    private async Task<Medication?> TryReadMedicationAsync(string medicationId, CancellationToken ct)
    {
        try
        {
            return await _client.ReadAsync<Medication>(new Uri($"Medication/{medicationId}", UriKind.Relative));
        }
        catch (FhirOperationException ex) when (ex.Status == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static List<T> Matches<T>(Bundle? bundle) where T : Resource
    {
        if (bundle?.Entry is null)
            return [];

        return bundle.Entry
            .Where(e => e.Search?.Mode is null or Bundle.SearchEntryMode.Match)
            .Select(e => e.Resource)
            .OfType<T>()
            .ToList();
    }

    private static Dictionary<string, Medication> IndexIncludedMedications(Bundle? bundle)
    {
        var map = new Dictionary<string, Medication>(StringComparer.Ordinal);
        if (bundle?.Entry is null)
            return map;

        foreach (var entry in bundle.Entry)
        {
            if (entry.Resource is not Medication med || string.IsNullOrEmpty(med.Id))
                continue;
            if (entry.Search?.Mode is null or Bundle.SearchEntryMode.Include)
                map.TryAdd(med.Id, med);
        }

        return map;
    }

    private static string? LogicalId(string reference)
    {
        var slash = reference.LastIndexOf('/');
        var id = slash >= 0 ? reference[(slash + 1)..] : reference;
        var hist = id.IndexOf("/_history", StringComparison.Ordinal);
        if (hist >= 0)
            id = id[..hist];
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    private void Log(string operation, string key, int resultCount, Stopwatch sw)
    {
        _logger.LogInformation(
            "operation={Operation} patient_id={PatientId} result_count={ResultCount} duration_ms={DurationMs}",
            operation, key, resultCount, sw.ElapsedMilliseconds);
    }

    private FhirQueryException Wrap(string operation, string key, Exception ex)
    {
        // Failures must be visible in stderr — successes alone can't explain transient errors seen by the model.
        _logger.LogWarning(ex, "operation={Operation} patient_id={PatientId} failed", operation, key);
        return new($"Failed to query FHIR server ({operation} for '{key}'): {ex.Message}", ex);
    }

    public void Dispose() => _client.Dispose();
}
