using System.Net;

namespace LeakOps.Workflow;

/// <summary>Puts the demo back to its start: no incidents, no work orders, no reservations, original stock.</summary>
public interface IDemoReset
{
    Task ResetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Calls the Waterstoring site, which owns the write access to the data. The workflow host stays a pure orchestrator that never touches
/// the data itself, so the reset is delegated with the same operator key the console already uses.
/// </summary>
public class WaterstoringDemoReset(HttpClient http, WorkflowSettings settings, IConfiguration configuration, IHostEnvironment environment) : IDemoReset
{
    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        var root = new Uri(new Uri(settings.WaterstoringPublicUrl).GetLeftPart(UriPartial.Authority));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(root, "/api/admin/reset-demo"));

        var operatorKey = configuration["OPERATOR_KEY"];
        if (!string.IsNullOrWhiteSpace(operatorKey)) request.Headers.Add("X-Operator-Key", operatorKey);
        // On a laptop Waterstoring has no operator key, but it accepts its well-known development ingest key.
        else if (environment.IsDevelopment()) request.Headers.Add("X-Api-Key", configuration["WATERSTORING_API_KEY"] ?? "dev-waterstoring-key");

        HttpResponseMessage response;
        try { response = await http.SendAsync(request, cancellationToken); }
        catch (HttpRequestException ex) { throw new InvalidOperationException($"Waterstoring could not be reached: {ex.Message}", ex); }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("Waterstoring refused the reset: its OPERATOR_KEY does not match this console's key.");
            response.EnsureSuccessStatusCode();
        }
    }
}
