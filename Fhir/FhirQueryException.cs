namespace fhir_mcp_server.Fhir;

/// <summary>Technical FHIR/HAPI failure — not the same as patient/resource not found.</summary>
public sealed class FhirQueryException : Exception
{
    public FhirQueryException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
