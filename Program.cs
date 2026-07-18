using fhir_mcp_server.Fhir;
using fhir_mcp_server.Services;
using fhir_mcp_server.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Dual-mode entry:
//   default     → MCP stdio host (Claude Desktop / MCP Inspector)
//   --smoke …   → Checkpoint C HAPI smoke (stdout OK; no MCP protocol)

var smoke = args.Contains("--smoke", StringComparer.OrdinalIgnoreCase);
var nameHint = args.SkipWhile(a => !string.Equals(a, "--smoke", StringComparison.OrdinalIgnoreCase))
    .Skip(1)
    .FirstOrDefault() ?? "Ab";

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables();

builder.Services.Configure<FhirOptions>(builder.Configuration.GetSection(FhirOptions.SectionName));
builder.Services.AddSingleton<IFhirRepository, FirelyFhirRepository>();
builder.Services.AddSingleton<IClinicalQueryService, ClinicalQueryService>();

if (smoke)
{
    using var host = builder.Build();
    var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Smoke");
    var clinical = host.Services.GetRequiredService<IClinicalQueryService>();

    Console.WriteLine($"=== Search patients name~'{nameHint}' ===");
    var search = await clinical.SearchPatientsAsync(nameHint);
    Console.WriteLine(search);
    Console.WriteLine();

    var ids = ExtractIds(search).Take(3).ToList();
    if (ids.Count == 0)
    {
        Console.WriteLine("No patient ids to smoke. Try a different --smoke nameHint.");
        return;
    }

    foreach (var id in ids)
    {
        Console.WriteLine($"=== Summary Patient/{id} ===");
        Console.WriteLine(await clinical.GetPatientSummaryAsync(id));
        Console.WriteLine();
        Console.WriteLine($"=== Conditions Patient/{id} ===");
        Console.WriteLine(await clinical.GetConditionsAsync(id));
        Console.WriteLine();
        Console.WriteLine($"=== Medications Patient/{id} ===");
        Console.WriteLine(await clinical.GetMedicationsAsync(id));
        Console.WriteLine();
        Console.WriteLine($"=== Observations laboratory Patient/{id} ===");
        Console.WriteLine(await clinical.GetObservationsAsync(id, "laboratory"));
        Console.WriteLine();
    }

    log.LogInformation("Smoke finished for nameHint={NameHint} patients={Count}", nameHint, ids.Count);
    return;
}

// MCP path: stdout is the JSON-RPC channel — keep it clean.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton<HealthcareTools>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<HealthcareTools>();

// ponytail: belt — drop any accidental Console.Out writes on the MCP path (smoke uses stdout above).
Console.SetOut(TextWriter.Null);

await builder.Build().RunAsync();

static IEnumerable<string> ExtractIds(string searchText)
{
    foreach (var line in searchText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        const string marker = "id=";
        var i = line.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0)
            continue;
        var rest = line[(i + marker.Length)..];
        var end = rest.IndexOf(';');
        yield return (end < 0 ? rest : rest[..end]).Trim();
    }
}
