using fhir_mcp_server.Fhir;

namespace fhir_mcp_server.Tests;

public class PatientIdTests
{
    [Theory]
    [InlineData("3506", "3506")]
    [InlineData("Patient/3506", "3506")]
    [InlineData("patient/3506", "3506")]
    [InlineData("  Patient/3506  ", "3506")]
    public void Normalize_AcceptsBareAndPrefixed(string raw, string expected) =>
        Assert.Equal(expected, PatientId.Normalize(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Patient/")]
    public void Normalize_Empty_ReturnsNull(string? raw) =>
        Assert.Null(PatientId.Normalize(raw));
}
