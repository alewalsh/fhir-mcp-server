using Hl7.Fhir.Model;
using fhir_mcp_server.Fhir;
using fhir_mcp_server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace fhir_mcp_server.Tests;

public class ClinicalQueryServiceTests
{
    [Fact]
    public async Task GetPatientSummary_NotFound()
    {
        var svc = new ClinicalQueryService(new FakeRepo(), NullLogger<ClinicalQueryService>.Instance);
        var text = await svc.GetPatientSummaryAsync("missing");
        Assert.Equal("Patient missing not found.", text);
    }

    [Fact]
    public async Task GetPatientSummary_NormalizesPrefixedId_AndComposes()
    {
        var svc = new ClinicalQueryService(new FakeRepo(), NullLogger<ClinicalQueryService>.Instance);
        var text = await svc.GetPatientSummaryAsync("Patient/p1");
        Assert.Contains("Demographics", text);
        Assert.Contains("Ada Lovelace", text);
        Assert.Contains("Hypertension", text);
        Assert.Contains("Lisinopril 10 MG Oral Tablet — active", text);
        Assert.DoesNotContain("resourceType", text);
    }

    [Fact]
    public async Task GetConditions_EmptyList_Message()
    {
        var svc = new ClinicalQueryService(new FakeRepo { EmptyClinical = true }, NullLogger<ClinicalQueryService>.Instance);
        var text = await svc.GetConditionsAsync("p1");
        Assert.Equal("No conditions found for patient p1.", text);
    }

    [Fact]
    public async Task GetObservations_InvalidCategory()
    {
        var svc = new ClinicalQueryService(new FakeRepo(), NullLogger<ClinicalQueryService>.Instance);
        var text = await svc.GetObservationsAsync("p1", "survey");
        Assert.StartsWith("Invalid category:", text);
    }

    [Fact]
    public async Task GetPatientSummary_TechnicalFailure_DistinctFromNotFound()
    {
        var svc = new ClinicalQueryService(new FakeRepo { ThrowOnGet = true }, NullLogger<ClinicalQueryService>.Instance);
        var text = await svc.GetPatientSummaryAsync("p1");
        Assert.StartsWith("Failed to query FHIR server:", text);
        Assert.DoesNotContain("not found", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetMedications_DedupesRenewals_AndReportsTruncation()
    {
        var svc = new ClinicalQueryService(new FakeRepo { ManyMedRenewals = true }, NullLogger<ClinicalQueryService>.Instance);
        var text = await svc.GetMedicationsAsync("p1");
        Assert.Contains("Simvastatin 20 MG Oral Tablet — active", text);
        Assert.DoesNotContain("stopped", text);
        // Fake returns Medications-cap rows → truncated note; unique drugs collapsed to 1.
        Assert.Contains("showing 1 of more…", text);
        Assert.Equal(1, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Count(l => l.StartsWith("- ")));
    }

    [Fact]
    public async Task SearchPatients_EmptyName_Invalid()
    {
        var svc = new ClinicalQueryService(new FakeRepo(), NullLogger<ClinicalQueryService>.Instance);
        Assert.Equal("Invalid name: name is required.", await svc.SearchPatientsAsync("  "));
    }

    [Fact]
    public async Task SearchPatients_NoMatch_TeachesNameOnlyLimitAtFailureTime()
    {
        // Runtime defense: a model that probes a clinical term as a name must
        // learn from the result — deterministic text, unlike description-only steering.
        var svc = new ClinicalQueryService(new FakeRepo(), NullLogger<ClinicalQueryService>.Instance);
        var text = await svc.SearchPatientsAsync("diabetes");
        Assert.Contains("No patients found with a name matching \"diabetes\"", text, StringComparison.Ordinal);
        Assert.Contains("matches patient names only", text, StringComparison.Ordinal);
        Assert.Contains("no such capability", text, StringComparison.Ordinal);
        Assert.Contains("Do not retry with other clinical terms", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchPatients_TruncatesAtCap()
    {
        var svc = new ClinicalQueryService(new FakeRepo { ManySearchHits = true }, NullLogger<ClinicalQueryService>.Instance);
        var text = await svc.SearchPatientsAsync("Ada");
        Assert.Contains("showing 10 of more…", text);
        Assert.Equal(10, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Count(l => l.StartsWith("- id=")));
    }

    [Fact]
    public async Task GetConditions_InvalidStatus()
    {
        var svc = new ClinicalQueryService(new FakeRepo(), NullLogger<ClinicalQueryService>.Instance);
        var text = await svc.GetConditionsAsync("p1", "inactive");
        Assert.StartsWith("Invalid status:", text);
    }

    [Fact]
    public async Task GetMedications_InvalidStatus()
    {
        var svc = new ClinicalQueryService(new FakeRepo(), NullLogger<ClinicalQueryService>.Instance);
        var text = await svc.GetMedicationsAsync("p1", "completed");
        Assert.StartsWith("Invalid status:", text);
    }

    [Fact]
    public async Task GetConditions_PatientNotFound()
    {
        var svc = new ClinicalQueryService(new FakeRepo(), NullLogger<ClinicalQueryService>.Instance);
        Assert.Equal("Patient missing not found.", await svc.GetConditionsAsync("missing"));
    }

    [Fact]
    public async Task GetPatientSummary_EmptyClinical_ShowsNoneSections()
    {
        var svc = new ClinicalQueryService(new FakeRepo { EmptyClinical = true }, NullLogger<ClinicalQueryService>.Instance);
        var text = await svc.GetPatientSummaryAsync("p1");
        Assert.Contains("Active conditions", text);
        Assert.Contains("Current medications", text);
        Assert.Contains("- None", text);
        Assert.DoesNotContain("resourceType", text);
    }

    [Fact]
    public async Task GetObservations_TruncatesAtCap()
    {
        var svc = new ClinicalQueryService(new FakeRepo { ManyObservations = true }, NullLogger<ClinicalQueryService>.Instance);
        var text = await svc.GetObservationsAsync("p1", "laboratory");
        Assert.Contains("showing 20 of more…", text);
        Assert.Equal(20, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Count(l => l.StartsWith("- ")));
    }

    [Fact]
    public async Task GetObservations_VitalSigns_Allowed()
    {
        var svc = new ClinicalQueryService(new FakeRepo { ManyObservations = true }, NullLogger<ClinicalQueryService>.Instance);
        var text = await svc.GetObservationsAsync("p1", "vital-signs");
        Assert.Contains("Observations (vital-signs)", text);
    }

    private sealed class FakeRepo : IFhirRepository
    {
        public bool EmptyClinical { get; init; }
        public bool ThrowOnGet { get; init; }
        public bool ManyMedRenewals { get; init; }
        public bool ManySearchHits { get; init; }
        public bool ManyObservations { get; init; }

        public Task<Patient?> GetPatientAsync(string patientId, CancellationToken ct = default)
        {
            if (ThrowOnGet)
                throw new FhirQueryException("HAPI down");
            if (patientId != "p1")
                return Task.FromResult<Patient?>(null);
            return Task.FromResult<Patient?>(new Patient
            {
                Id = "p1",
                Gender = AdministrativeGender.Female,
                BirthDate = "1815-12-10",
                Name = [new HumanName { Use = HumanName.NameUse.Official, Family = "Lovelace", Given = ["Ada"] }]
            });
        }

        public Task<IReadOnlyList<Patient>> SearchPatientsByNameAsync(string name, int count = FhirQueryLimits.PatientSearch, CancellationToken ct = default)
        {
            if (!ManySearchHits)
                return Task.FromResult<IReadOnlyList<Patient>>([]);

            var list = Enumerable.Range(0, count)
                .Select(i => new Patient
                {
                    Id = $"p{i}",
                    Name = [new HumanName { Family = "Hit", Given = [$"P{i}"] }]
                })
                .ToList();
            return Task.FromResult<IReadOnlyList<Patient>>(list);
        }

        public Task<IReadOnlyList<Condition>> SearchConditionsAsync(string patientId, string? clinicalStatus = null, int count = FhirQueryLimits.Conditions, CancellationToken ct = default)
        {
            if (EmptyClinical)
                return Task.FromResult<IReadOnlyList<Condition>>([]);
            return Task.FromResult<IReadOnlyList<Condition>>(
            [
                new Condition
                {
                    ClinicalStatus = new CodeableConcept("http://terminology.hl7.org/CodeSystem/condition-clinical", "active"),
                    Code = new CodeableConcept { Text = "Hypertension" },
                    Onset = new FhirDateTime("2020-01-01")
                }
            ]);
        }

        public Task<IReadOnlyList<(MedicationRequest Request, Medication? IncludedMedication)>> SearchMedicationRequestsAsync(
            string patientId, string? status = null, int count = FhirQueryLimits.Medications, CancellationToken ct = default)
        {
            if (EmptyClinical)
                return Task.FromResult<IReadOnlyList<(MedicationRequest, Medication?)>>([]);

            if (ManyMedRenewals)
            {
                var list = new List<(MedicationRequest, Medication?)>();
                if (status is null or "stopped")
                {
                    for (var i = 0; i < FhirQueryLimits.Medications; i++)
                    {
                        list.Add((new MedicationRequest
                        {
                            Status = MedicationRequest.MedicationrequestStatus.Stopped,
                            AuthoredOn = $"2020-01-{(i % 28) + 1:D2}",
                            Medication = new CodeableConcept { Text = "Simvastatin 20 MG Oral Tablet" }
                        }, null));
                    }
                }

                if (status is null or "active")
                {
                    list.Add((new MedicationRequest
                    {
                        Status = MedicationRequest.MedicationrequestStatus.Active,
                        AuthoredOn = "2019-01-01", // older than stopped renewals — active must still win
                        Medication = new CodeableConcept { Text = "Simvastatin 20 MG Oral Tablet" }
                    }, null));
                }

                return Task.FromResult<IReadOnlyList<(MedicationRequest, Medication?)>>(list);
            }

            return Task.FromResult<IReadOnlyList<(MedicationRequest, Medication?)>>(
            [
                (new MedicationRequest
                {
                    Status = MedicationRequest.MedicationrequestStatus.Active,
                    Medication = new CodeableConcept { Text = "Lisinopril 10 MG Oral Tablet" },
                    DosageInstruction = [new Dosage { Text = "1 tablet daily" }]
                }, null)
            ]);
        }

        public Task<IReadOnlyList<Observation>> SearchObservationsAsync(
            string patientId, string category, int count = FhirQueryLimits.Observations, CancellationToken ct = default)
        {
            if (!ManyObservations)
                return Task.FromResult<IReadOnlyList<Observation>>([]);

            var list = Enumerable.Range(0, count)
                .Select(i => new Observation
                {
                    Code = new CodeableConcept { Text = $"Lab {i}" },
                    Value = new Quantity { Value = i, Unit = "mg/dL" },
                    Effective = new FhirDateTime($"2024-01-{(i % 28) + 1:D2}")
                })
                .ToList();
            return Task.FromResult<IReadOnlyList<Observation>>(list);
        }
    }
}
