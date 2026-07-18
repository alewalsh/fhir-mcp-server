namespace fhir_mcp_server.Fhir;

public static class PatientId
{
    /// <summary>Accepts bare id or Patient/{id}; returns bare logical id. Empty/whitespace → null.</summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var id = raw.Trim();
        const string prefix = "Patient/";
        if (id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            id = id[prefix.Length..];

        return string.IsNullOrWhiteSpace(id) ? null : id;
    }
}
