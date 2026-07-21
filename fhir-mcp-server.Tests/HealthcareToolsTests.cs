using System.ComponentModel;
using System.Reflection;
using fhir_mcp_server.Services;
using fhir_mcp_server.Tools;
using ModelContextProtocol.Server;
using Task = System.Threading.Tasks.Task;

namespace fhir_mcp_server.Tests;

public class HealthcareToolsTests
{
    // CI guards contract text only.
    // The heavy lifting lives in the runtime no-results message (see ClinicalQueryServiceTests),
    // which the model reads at failure time; the description stays short.
    [Fact]
    public void SearchPatients_Description_StatesCannotSearchByCondition()
    {
        var desc = MethodDescription(nameof(HealthcareTools.SearchPatients));
        Assert.Contains("cannot search by condition", desc, StringComparison.Ordinal);
    }

    public static TheoryData<string> McpToolMethodNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in typeof(HealthcareTools)
                     .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                     .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
                     .Select(m => m.Name))
        {
            data.Add(name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(McpToolMethodNames))]
    public void EveryMcpTool_Description_IncludesClinicalUseDisclaimer(string methodName)
    {
        var desc = MethodDescription(methodName);
        Assert.Contains("not for clinical use", desc, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(McpToolMethodNames))]
    public void EveryMcpTool_IsReadOnlyAndNonDestructive(string methodName)
    {
        var method = typeof(HealthcareTools).GetMethod(methodName)
            ?? throw new InvalidOperationException($"Method {methodName} not found.");
        var attr = method.GetCustomAttribute<McpServerToolAttribute>()
            ?? throw new InvalidOperationException($"{methodName} missing [McpServerTool].");
        Assert.True(attr.ReadOnly, $"{methodName} must be ReadOnly=true.");
        // SDK default Destructive=true; leave it unset and Inspector shows ✓ Destructive next to ✓ Read-only.
        Assert.False(attr.Destructive, $"{methodName} must be Destructive=false.");
    }

    [Fact]
    public void OnlyHealthcareTools_IsMcpServerToolType()
    {
        var toolTypes = typeof(HealthcareTools).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .ToList();
        Assert.Single(toolTypes);
        Assert.Equal(typeof(HealthcareTools), toolTypes[0]);
    }

    private static string MethodDescription(string methodName)
    {
        var method = typeof(HealthcareTools).GetMethod(methodName)
            ?? throw new InvalidOperationException($"Method {methodName} not found.");
        var attr = method.GetCustomAttribute<DescriptionAttribute>()
            ?? throw new InvalidOperationException($"{methodName} missing [Description].");
        return attr.Description;
    }

    [Fact]
    public async Task SearchPatients_DelegatesToService()
    {
        var fake = new FakeClinical { SearchResult = "MATCH" };
        var tools = new HealthcareTools(fake);
        Assert.Equal("MATCH", await tools.SearchPatients("Ada"));
        Assert.Equal("Ada", fake.LastName);
    }

    [Fact]
    public async Task GetPatientSummary_NotFound_PassesThrough()
    {
        var fake = new FakeClinical { SummaryResult = "Patient missing not found." };
        var tools = new HealthcareTools(fake);
        Assert.Equal("Patient missing not found.", await tools.GetPatientSummary("missing"));
        Assert.Equal("missing", fake.LastPatientId);
    }

    [Fact]
    public async Task GetConditions_DelegatesStatus()
    {
        var fake = new FakeClinical { ConditionsResult = "CONDS" };
        var tools = new HealthcareTools(fake);
        Assert.Equal("CONDS", await tools.GetConditions("p1", "active"));
        Assert.Equal(("p1", "active"), fake.LastConditionsArgs);
    }

    [Fact]
    public async Task GetMedications_Delegates()
    {
        var fake = new FakeClinical { MedicationsResult = "MEDS" };
        var tools = new HealthcareTools(fake);
        Assert.Equal("MEDS", await tools.GetMedications("p1"));
        Assert.Equal(("p1", (string?)null), fake.LastMedicationsArgs);
    }

    [Fact]
    public async Task GetObservations_DelegatesCategory()
    {
        var fake = new FakeClinical { ObservationsResult = "OBS" };
        var tools = new HealthcareTools(fake);
        Assert.Equal("OBS", await tools.GetObservations("p1", "laboratory"));
        Assert.Equal(("p1", "laboratory"), fake.LastObservationsArgs);
    }

    private sealed class FakeClinical : IClinicalQueryService
    {
        public string SearchResult { get; init; } = "";
        public string SummaryResult { get; init; } = "";
        public string ConditionsResult { get; init; } = "";
        public string MedicationsResult { get; init; } = "";
        public string ObservationsResult { get; init; } = "";

        public string? LastName { get; private set; }
        public string? LastPatientId { get; private set; }
        public (string PatientId, string? Status)? LastConditionsArgs { get; private set; }
        public (string PatientId, string? Status)? LastMedicationsArgs { get; private set; }
        public (string PatientId, string Category)? LastObservationsArgs { get; private set; }

        public Task<string> SearchPatientsAsync(string name, CancellationToken ct = default)
        {
            LastName = name;
            return Task.FromResult(SearchResult);
        }

        public Task<string> GetPatientSummaryAsync(string patientId, CancellationToken ct = default)
        {
            LastPatientId = patientId;
            return Task.FromResult(SummaryResult);
        }

        public Task<string> GetConditionsAsync(string patientId, string? status = null, CancellationToken ct = default)
        {
            LastConditionsArgs = (patientId, status);
            return Task.FromResult(ConditionsResult);
        }

        public Task<string> GetMedicationsAsync(string patientId, string? status = null, CancellationToken ct = default)
        {
            LastMedicationsArgs = (patientId, status);
            return Task.FromResult(MedicationsResult);
        }

        public Task<string> GetObservationsAsync(string patientId, string category, CancellationToken ct = default)
        {
            LastObservationsArgs = (patientId, category);
            return Task.FromResult(ObservationsResult);
        }
    }
}
