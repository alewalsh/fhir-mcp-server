using Hl7.Fhir.Model;
using fhir_mcp_server.Mapping;
using fhir_mcp_server.Mapping.Models;

namespace fhir_mcp_server.Tests;

public class MapperTests
{
    [Fact]
    public void Patient_MapsNameGenderBirthAge()
    {
        var patient = new Patient
        {
            Id = "1",
            Gender = AdministrativeGender.Male,
            BirthDate = "1990-01-15",
            Name =
            [
                new HumanName
                {
                    Use = HumanName.NameUse.Official,
                    Family = "Doe",
                    Given = ["Jane"]
                }
            ]
        };

        var s = PatientSummaryMapper.ToSummary(patient);
        Assert.Equal("1", s.Id);
        Assert.Equal("Jane Doe", s.Name);
        Assert.Equal("male", s.Gender);
        Assert.Equal("1990-01-15", s.BirthDate);
        Assert.NotNull(s.AgeYears);
        Assert.True(s.AgeYears >= 30);
    }

    [Fact]
    public void Patient_MissingName_DoesNotFabricate()
    {
        var s = PatientSummaryMapper.ToSummary(new Patient { Id = "2" });
        Assert.Null(s.Name);
        Assert.Null(s.BirthDate);
        Assert.Null(s.AgeYears);
    }

    [Fact]
    public void Condition_MapsDisplayStatusOnset()
    {
        var c = new Condition
        {
            ClinicalStatus = new CodeableConcept("http://terminology.hl7.org/CodeSystem/condition-clinical", "active"),
            Code = new CodeableConcept
            {
                Text = "Essential hypertension",
                Coding = [new Coding("http://snomed.info/sct", "59621000", "Essential hypertension")]
            },
            Onset = new FhirDateTime("2018-03-01")
        };

        var s = ConditionSummaryMapper.ToSummary(c);
        Assert.Equal("Essential hypertension", s.Display);
        Assert.Equal("active", s.Status);
        Assert.Equal("2018-03-01", s.OnsetDate);
    }

    [Fact]
    public void Condition_MissingOnset_LeavesNull()
    {
        var s = ConditionSummaryMapper.ToSummary(new Condition
        {
            Code = new CodeableConcept { Text = "Asthma" }
        });
        Assert.Equal("Asthma", s.Display);
        Assert.Null(s.OnsetDate);
        Assert.Null(s.Status);
    }

    [Fact]
    public void Medication_CodeableAndDose()
    {
        var mr = new MedicationRequest
        {
            Status = MedicationRequest.MedicationrequestStatus.Active,
            Medication = new CodeableConcept { Text = "Lisinopril 10 MG Oral Tablet" },
            DosageInstruction = [new Dosage { Text = "Take 1 tablet daily" }]
        };

        var s = MedicationSummaryMapper.ToSummary(mr);
        Assert.Equal("Lisinopril 10 MG Oral Tablet", s.Name);
        Assert.Equal("Take 1 tablet daily", s.Dose);
        Assert.Equal("active", s.Status);
    }

    [Fact]
    public void Medication_ReferenceOnly_UsesIncludedOrUnresolved()
    {
        var mr = new MedicationRequest
        {
            Medication = new ResourceReference("Medication/99")
        };

        var unresolved = MedicationSummaryMapper.ToSummary(mr);
        Assert.Equal("Medication (unresolved reference)", unresolved.Name);
        Assert.Null(unresolved.Dose);

        var included = new Medication
        {
            Id = "99",
            Code = new CodeableConcept { Text = "Aspirin 81 MG" }
        };
        var resolved = MedicationSummaryMapper.ToSummary(mr, included);
        Assert.Equal("Aspirin 81 MG", resolved.Name);
    }

    [Fact]
    public void Observation_MapsValueAndMissingValue()
    {
        var withValue = ObservationSummaryMapper.ToSummary(new Observation
        {
            Code = new CodeableConcept { Text = "Body Weight" },
            Value = new Quantity { Value = 80, Unit = "kg" },
            Effective = new FhirDateTime("2024-01-02")
        });
        Assert.Equal("Body Weight", withValue.Display);
        Assert.Equal("80 kg", withValue.Value);
        Assert.Equal("2024-01-02", withValue.EffectiveDate);

        var gap = ObservationSummaryMapper.ToSummary(new Observation
        {
            Code = new CodeableConcept { Text = "Note" }
        });
        Assert.Equal("Note", gap.Display);
        Assert.Null(gap.Value);
    }

    [Fact]
    public void Medication_DedupeByDrug_ActiveWinsEvenIfNotNewestAuthored()
    {
        var items = new (MedicationRequest, Medication?)[]
        {
            (new MedicationRequest
            {
                Status = MedicationRequest.MedicationrequestStatus.Active,
                AuthoredOn = "2020-01-01",
                Medication = new CodeableConcept { Text = "Simvastatin 20 MG Oral Tablet" }
            }, null),
            (new MedicationRequest
            {
                Status = MedicationRequest.MedicationrequestStatus.Stopped,
                AuthoredOn = "2024-12-01",
                Medication = new CodeableConcept { Text = "Simvastatin 20 MG Oral Tablet" }
            }, null),
        };

        var deduped = MedicationSummaryMapper.ToDedupedSummaries(items);
        var simva = Assert.Single(deduped);
        Assert.Equal("active", simva.Status);
    }

    [Fact]
    public void Medication_DedupeByDrug_KeepsLatestAuthoredStatus()
    {
        var items = new (MedicationRequest, Medication?)[]
        {
            (new MedicationRequest
            {
                Status = MedicationRequest.MedicationrequestStatus.Stopped,
                AuthoredOn = "2020-01-01",
                Medication = new CodeableConcept { Text = "Simvastatin 20 MG Oral Tablet" }
            }, null),
            (new MedicationRequest
            {
                Status = MedicationRequest.MedicationrequestStatus.Stopped,
                AuthoredOn = "2021-06-01",
                Medication = new CodeableConcept { Text = "Simvastatin 20 MG Oral Tablet" }
            }, null),
            (new MedicationRequest
            {
                Status = MedicationRequest.MedicationrequestStatus.Active,
                AuthoredOn = "2024-03-15",
                Medication = new CodeableConcept { Text = "Simvastatin 20 MG Oral Tablet" }
            }, null),
            (new MedicationRequest
            {
                Status = MedicationRequest.MedicationrequestStatus.Active,
                AuthoredOn = "2023-01-01",
                Medication = new CodeableConcept { Text = "Amlodipine 5 MG Oral Tablet" }
            }, null),
        };

        var deduped = MedicationSummaryMapper.ToDedupedSummaries(items);
        Assert.Equal(2, deduped.Count);
        var simva = Assert.Single(deduped, m => m.Name!.Contains("Simvastatin", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("active", simva.Status);
        Assert.Contains(deduped, m => m.Name!.Contains("Amlodipine", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Formatter_TruncationAndMedicationDashStatus()
    {
        var meds = new List<MedicationSummary>
        {
            new() { Name = "Simvastatin 20 MG Oral Tablet", Status = "active" }
        };
        var text = SummaryFormatter.FormatMedicationList("9", meds, truncated: true);
        Assert.Contains("showing 1 of more…", text);
        Assert.Contains("Simvastatin 20 MG Oral Tablet — active", text);

        var conditions = new List<ConditionSummary>
        {
            new() { Display = "Asthma", Status = "active" }
        };
        Assert.Contains(
            "showing 1 of more…",
            SummaryFormatter.FormatConditionList("9", conditions, truncated: true));
        Assert.Contains(
            "showing 1 of more…",
            SummaryFormatter.FormatObservationList("9", "laboratory",
                [new ObservationSummary { Display = "Glucose", Value = "90", EffectiveDate = "2020-01-01" }],
                truncated: true));
    }

    [Fact]
    public void Formatter_SummaryUsesNoneForEmptySections()
    {
        var text = SummaryFormatter.FormatPatientSummary(
            new PatientSummary { Id = "9", Name = "Pat" },
            [],
            []);

        Assert.Contains("Active conditions", text);
        Assert.Contains("Current medications", text);
        Assert.Contains("- None", text);
        Assert.DoesNotContain("{", text);
        Assert.DoesNotContain("resourceType", text);
    }
}
