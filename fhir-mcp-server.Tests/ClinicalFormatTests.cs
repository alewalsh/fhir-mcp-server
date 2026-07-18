using System.Globalization;
using Hl7.Fhir.Model;
using fhir_mcp_server.Mapping;

namespace fhir_mcp_server.Tests;

public class ClinicalFormatTests
{
    [Fact]
    public void Quantity_UsesInvariantDecimalSeparator_EvenUnderEsArCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-AR");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-AR");
        try
        {
            var q = new Quantity { Value = 5.81m, Unit = "%" };
            Assert.Equal("5.81 %", ClinicalFormat.Quantity(q));

            var obs = ObservationSummaryMapper.ToSummary(new Observation
            {
                Code = new CodeableConcept { Text = "Hemoglobin A1c" },
                Value = q,
                Effective = new FhirDateTime("2001-12-21T03:14:47-05:00")
            });
            Assert.Equal("5.81 %", obs.Value);
            Assert.DoesNotContain(",", obs.Value);
            Assert.Equal("2001-12-21T03:14:47-05:00", obs.EffectiveDate);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Date_NormalizesDateOnlyAndParsesInvariant()
    {
        Assert.Equal("1952-12-05", ClinicalFormat.Date("1952-12-05"));
        Assert.True(ClinicalFormat.TryParseDateTime("1952-12-05", out var dob));
        Assert.Equal(1952, dob.Year);
        Assert.Equal(12, dob.Month);
        Assert.Equal(5, dob.Day);
    }
}
