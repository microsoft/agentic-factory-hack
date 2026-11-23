#:package ModelContextProtocol@2.2.0

// Walks the burst scenario through the MCP server the way the six agents will, plus the guardrail cases.
// Usage: dotnet run evides-leakops/scripts/mcp-smoke.cs
//   MCP_SERVER_URL   default http://localhost:7071/runtime/webhooks/mcp
//   MCP_AUTH_KEY     optional, sent as header x-functions-key (needed for the deployed function app)
// It writes reservations, purchase orders and a work order for incident INC-SMOKE-1. Clean up afterwards with:
//   dotnet run --project evides-leakops/src/LeakOps.Seeder -- --reset-runtime

using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var endpoint = new Uri(Environment.GetEnvironmentVariable("MCP_SERVER_URL") ?? "http://localhost:7071/runtime/webhooks/mcp");
var options = new HttpClientTransportOptions { Endpoint = endpoint };
if (Environment.GetEnvironmentVariable("MCP_AUTH_KEY") is { Length: > 0 } key)
    options.AdditionalHeaders = new Dictionary<string, string> { ["x-functions-key"] = key };

await using var client = await McpClient.CreateAsync(new HttpClientTransport(options));

var tools = await client.ListToolsAsync();
Console.WriteLine($"TOOLS ({tools.Count}): " + string.Join(", ", tools.Select(t => t.Name).Order()));

const string incident = "INC-SMOKE-1";
const string alarm = "2026-11-14T03:10:00Z";
var failures = 0;

async Task<JsonElement> Call(string name, Dictionary<string, object?> args, int show = 300)
{
    var result = await client.CallToolAsync(name, args);
    var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
    Console.WriteLine($"\n== {name}");
    Console.WriteLine(text.Length <= show ? text : text[..show] + $" ... ({text.Length} chars)");
    return JsonDocument.Parse(text).RootElement.Clone();
}

void Expect(bool condition, string what)
{
    Console.WriteLine($"   {(condition ? "OK  " : "FAIL")} {what}");
    if (!condition) failures++;
}

string? Text(JsonElement e, string property) => e.TryGetProperty(property, out var p) ? p.GetString() : null;

// ---- Triage
var sensors = await Call("list-sensors", new() { ["districtId"] = "D-01" });
Expect(sensors.GetProperty("sensors").GetArrayLength() == 4, "D-01 has 4 sensors");
var readings = await Call("get-sensor-readings", new() { ["sensorId"] = "PS-101", ["untilUtc"] = alarm, ["hours"] = 1 }, 450);
Expect(readings.GetProperty("summary").GetProperty("last").GetProperty("v").GetDouble() == 2.85, "PS-101 ends at 2.85 bar");
var works = await Call("get-planned-works", new() { ["districtId"] = "D-01", ["atUtc"] = alarm });
Expect(works.GetProperty("count").GetInt32() == 0, "no planned works in D-01 at the alarm time");
var weather = await Call("get-weather", new() { ["districtId"] = "D-01", ["untilUtc"] = alarm, ["hours"] = 6 }, 200);
Expect(weather.GetProperty("summary").GetProperty("latest").GetProperty("frost").GetBoolean(), "frost at the alarm time");
var usage = await Call("get-usage-history", new() { ["districtId"] = "D-01", ["atUtc"] = alarm }, 200);
Expect(Text(usage, "dayType") == "weekend", "Saturday at 04:10 local counts as weekend");

// ---- Diagnostic
var point = await Call("get-point-on-segment", new() { ["segmentId"] = "SEG-0412", ["chainageM"] = 160 }, 400);
Expect(point.GetProperty("isolationValves").GetArrayLength() == 2, "two isolation valves");
var badPoint = await Call("get-point-on-segment", new() { ["segmentId"] = "SEG-0412", ["chainageM"] = 9999 });
Expect(badPoint.TryGetProperty("error", out _), "position beyond the segment is rejected");
await Call("get-nearby-valves", new() { ["lat"] = 51.8608, ["lon"] = 4.3303, ["radiusM"] = 400 }, 250);
var ranking = await Call("rank-segments-by-pressure-drop", new() { ["districtId"] = "D-01", ["untilUtc"] = alarm }, 350);
Expect(Text(ranking.GetProperty("ranking")[0], "segmentId") == "SEG-0412" && ranking.GetProperty("ranking")[0].GetProperty("maxDropBar").GetDouble() > 1.5, "SEG-0412 has the strongest pressure drop of the district");
var estimate = await Call("estimate-leak-position", new() { ["segmentId"] = "SEG-0412", ["untilUtc"] = alarm }, 500);
Expect(Math.Abs(estimate.GetProperty("estimatedChainageM").GetDouble() - 160) <= 30, "estimated leak position is within 30 m of the simulated break (160 m)");
Expect(estimate.GetProperty("inletFlow").GetProperty("excessOverExpectedM3h").GetDouble() > 40, "inlet flow is more than 40 m3/h above normal");
Expect(Text(await Call("classify-severity", new() { ["segmentId"] = "SEG-0412", ["breachType"] = "burst" }, 300), "severity") == "S1", "a burst here is S1 (care home, 1840 customers)");
Expect((await Call("classify-severity", new() { ["segmentId"] = "SEG-0412", ["breachType"] = "explosion" })).TryGetProperty("error", out _), "unknown breach type is rejected");
var procedure = await Call("get-repair-procedure", new() { ["material"] = "cast_iron", ["diameterMm"] = 300 }, 200);
Expect(Text(procedure, "id") == "RP-CI-300-400-BURST", "cast iron DN300 burst procedure found");

// ---- Matchmaker
var certs = string.Join(",", procedure.GetProperty("requiredCertifications").EnumerateArray().Select(c => c.GetString()));
var technicians = await Call("get-technicians", new() { ["atUtc"] = alarm, ["requiredCertifications"] = certs, ["siteLat"] = 51.8608, ["siteLon"] = 4.3303 }, 200);
Expect(Text(technicians.GetProperty("technicians")[0], "id") == "T-02", "T-02 is the first available, fully certified technician");

// ---- Procurement
foreach (var (sku, qty) in new[] { ("CLP-CI-300", 1), ("GSK-300", 1), ("BLT-KIT-300", 1), ("DIS-KIT", 1), ("EQP-EXC-8T", 1), ("EQP-TRK-01", 1), ("EQP-PMP-01", 1) })
{
    var reserved = await Call("reserve-stock", new() { ["incidentId"] = incident, ["sku"] = sku, ["quantity"] = qty, ["atUtc"] = alarm }, 120);
    // "already_reserved" is also fine: it means this script ran before and the demo data was not reset yet.
    Expect(Text(reserved, "status") is "reserved" or "already_reserved", $"{sku} reserved");
}
Expect(Text(await Call("reserve-stock", new() { ["incidentId"] = incident, ["sku"] = "CLP-CI-300", ["quantity"] = 1 }, 120), "status") == "already_reserved", "repeating a reservation changes nothing");
Expect(Text(await Call("reserve-stock", new() { ["incidentId"] = "INC-SMOKE-2", ["sku"] = "CLP-CI-300", ["quantity"] = 5, ["atUtc"] = alarm }, 200), "status") == "insufficient_stock", "asking for more than is available is refused");
Expect(Text(await Call("reserve-stock", new() { ["incidentId"] = "INC-SMOKE-2", ["sku"] = "EQP-EXC-14T", ["quantity"] = 1, ["atUtc"] = alarm }, 200), "status") == "equipment_unavailable", "excavator that is still in service is refused");
var po = await Call("create-purchase-order", new() { ["incidentId"] = incident, ["sku"] = "VLV-GATE-300", ["quantity"] = 1, ["express"] = true, ["reason"] = "Isolation valve V-0412-B is old", ["orderedAtUtc"] = alarm }, 250);
Expect(Text(po.GetProperty("order"), "status") == "pending_approval", "express valve order needs approval (3010 EUR)");

// ---- Scheduling
var travel = await Call("get-travel-estimate", new() { ["fromLat"] = 51.8180, ["fromLon"] = 4.3500, ["toLat"] = 51.8608, ["toLon"] = 4.3303, ["departUtc"] = "2026-11-14T03:15:00Z", ["viaLat"] = 51.8420, ["viaLon"] = 4.3300 }, 250);
Expect(travel.GetProperty("legs").GetArrayLength() == 2, "route via the depot has two legs");
var refused = await Call("create-work-order", new() { ["incidentId"] = incident, ["segmentId"] = "SEG-0412", ["chainageM"] = 160, ["technicianId"] = "T-01", ["scheduledStartUtc"] = "2026-11-14T03:15:00Z", ["travelMinutes"] = 30, ["severity"] = "S2", ["summary"] = "Burst on DN300 cast iron" }, 250);
Expect(refused.TryGetProperty("error", out _), "uncertified technician T-01 is refused");
var order = await Call("create-work-order", new() { ["incidentId"] = incident, ["segmentId"] = "SEG-0412", ["chainageM"] = 160, ["technicianId"] = "T-02", ["scheduledStartUtc"] = "2026-11-14T03:15:00Z", ["travelMinutes"] = 41, ["isolationMinutes"] = 45, ["severity"] = "S2", ["summary"] = "Burst on DN300 cast iron main, close V-0412-A/B, fit full-circle clamp.", ["notes"] = "Frost: expect brittle pipe." }, 200);
Expect(order.GetProperty("workOrder").GetProperty("lines").GetArrayLength() == 7, "work order carries the 7 reserved parts and equipment");
Expect(order.GetProperty("expectedCompletionUtc").GetString()!.StartsWith("2026-11-14T10:41"), "completion = depart 03:15 + 41 min travel + 45 min isolation + 6 h repair");

Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
return failures == 0 ? 0 : 1;
