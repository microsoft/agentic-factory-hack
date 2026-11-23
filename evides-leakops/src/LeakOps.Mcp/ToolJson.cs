using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using LeakOps.Contracts.Logic;

namespace LeakOps.Mcp;

/// <summary>Turns tool results into compact JSON text (nulls left out, so the model reads fewer tokens) and parses tool arguments.</summary>
public static class ToolJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // keep apostrophes and quotes readable; the text goes to a model, not into HTML
    };

    public static string Ok(object value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Tools return errors as JSON instead of throwing, so the agent can read the message and correct itself.</summary>
    public static string Error(string message, object? hint = null) => JsonSerializer.Serialize(new { error = message, hint }, Options);

    public static bool TryTime(string? text, string argument, out DateTimeOffset utc, out string error)
    {
        error = "";
        if (LeakOpsTime.TryParseUtc(text, out utc)) return true;
        error = Error($"'{argument}' must be an ISO 8601 UTC timestamp, for example 2026-11-14T03:10:00Z.");
        return false;
    }

    /// <summary>Splits "A, B ,C" into a list; null or empty text gives an empty list.</summary>
    public static List<string> SplitList(string? csv) =>
        (csv ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
