using Fifa2026.V2.Functions.Data;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// .NET 8 isolated worker entrypoint.
// ConfigureFunctionsWebApplication enables ASP.NET Core integration
// (HttpRequest/IActionResult) in HTTP triggers.
var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        // Application Insights — distributed tracing (W3C Trace Context, ADE-000 Inv 5).
        // The Activity API is fed automatically by the Functions/ServiceBus SDKs.
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // SQL purchase repository (parameterized queries via Dapper + Microsoft.Data.SqlClient).
        services.AddSingleton<IPurchaseRepository, PurchaseRepository>();
    })
    .Build();

host.Run();
