using System.Net.Http.Json;
using System.Text.Json;
using LeakOps.Contracts;

namespace LeakOps.Workflow;

/// <summary>Calls the Agents function app: POST {base}/api/agents/{step}. In Azure the function key travels in the x-functions-key header.</summary>
public class AgentGateway(HttpClient http, WorkflowSettings settings) : IAgentGateway
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AgentResult<T>> CallAsync<T>(string step, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.AgentsBaseUrl, $"/api/agents/{step}"))
        {
            Content = JsonContent.Create(body, body.GetType(), options: Json),
        };
        if (settings.AgentsFunctionKey is not null) request.Headers.Add("x-functions-key", settings.AgentsFunctionKey);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new AgentCallException(step, 0, "unreachable", $"The {step} agent could not be reached: {ex.Message}");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var (reason, message) = ReadError(text, step);
                throw new AgentCallException(step, (int)response.StatusCode, reason, message);
            }
            return JsonSerializer.Deserialize<AgentResult<T>>(text, Json)
                   ?? throw new AgentCallException(step, (int)response.StatusCode, "empty_answer", $"The {step} agent returned an empty answer.");
        }
    }

    /// <summary>The agents answer errors as { error, reason, detail } (or { error, reviewReasons }).</summary>
    private static (string Reason, string Message) ReadError(string text, string step)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var reason = root.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString()! : "error";
            var error = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString()! : $"The {step} agent failed.";
            var detail = root.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String ? $" {d.GetString()}" : "";
            return (reason, error + detail);
        }
        catch (JsonException)
        {
            return ("error", string.IsNullOrWhiteSpace(text) ? $"The {step} agent failed." : text.Length > 300 ? text[..300] : text);
        }
    }
}
