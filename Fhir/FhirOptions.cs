namespace fhir_mcp_server.Fhir;

public sealed class FhirOptions
{
    public const string SectionName = "Fhir";

    /// <summary>HAPI FHIR base URL, e.g. http://127.0.0.1:8080/fhir</summary>
    public string BaseUrl { get; set; } = "";
}
