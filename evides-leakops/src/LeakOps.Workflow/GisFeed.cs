using System.Net.Http.Json;
using LeakOps.Contracts;

namespace LeakOps.Workflow;

/// <summary>
/// Sends the Diagnostic report to the Waterstoring site, which hosts the simulated GIS page. Like the demo reset, the workflow host does
/// not write data itself: it hands the report over with the operator key. Failures are logged and swallowed, the map is only a view.
/// </summary>
public class WaterstoringGisFeed(HttpClient http, WorkflowSettings settings, IConfiguration configuration, IHostEnvironment environment,
    ILogger<WaterstoringGisFeed> logger) : IGisFeed
{
    public async Task PublishAsync(DiagnosticReport report, CancellationToken cancellationToken)
    {
        try
        {
            var root = new Uri(new Uri(settings.WaterstoringPublicUrl).GetLeftPart(UriPartial.Authority));
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(root, "/api/gis/diagnosis")) { Content = JsonContent.Create(report) };

            var operatorKey = configuration["OPERATOR_KEY"];
            if (!string.IsNullOrWhiteSpace(operatorKey)) request.Headers.Add("X-Operator-Key", operatorKey);
            // On a laptop Waterstoring has no operator key, but it accepts its well-known development ingest key.
            else if (environment.IsDevelopment()) request.Headers.Add("X-Api-Key", configuration["WATERSTORING_API_KEY"] ?? "dev-waterstoring-key");

            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("The GIS page did not take the diagnosis for {AlarmId}: {Status}", report.AlarmId, (int)response.StatusCode);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("The GIS page could not be updated for {AlarmId}: {Message}", report.AlarmId, ex.Message);
        }
    }
}
