using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;

// Foundry Skills (preview): a skill is a versioned folder with a SKILL.md, stored centrally in the Foundry project (REST: {project}/skills).
// The registry publishes the skills in evides-leakops/skills when their text changed and then injects the default version into the
// instructions of the agents that use them ("direct injection"). A new skill version therefore also gives the agent a new version.

/// <summary>A skill as it is stored in Foundry: the version that is the default and the instructions in its SKILL.md.</summary>
public record SkillVersion(string Name, string Version, string Description, string Body, bool Published);

public class SkillStore(HttpClient http, Uri projectEndpoint, TokenCredential credential, string skillsDirectory)
{
    private static readonly string[] Scope = ["https://ai.azure.com/.default"];

    /// <summary>
    /// Makes sure the local SKILL.md is the default version in Foundry (publishing a new version only when the text differs) and
    /// returns that version. Foundry's own copy is read back, so what the agent gets is what is stored in the project.
    /// </summary>
    public async Task<SkillVersion> EnsureAsync(string name, CancellationToken cancellationToken)
    {
        var path = Path.Combine(skillsDirectory, name, "SKILL.md");
        if (!File.Exists(path)) throw new FileNotFoundException($"Skill '{name}' has no SKILL.md in {skillsDirectory}.", path);
        var local = Normalize(await File.ReadAllTextAsync(path, cancellationToken));

        var published = false;
        var defaultVersion = await GetDefaultVersionAsync(name, cancellationToken);
        var stored = defaultVersion is null ? null : await ReadSkillMdAsync(name, defaultVersion, cancellationToken);
        if (stored is null || Normalize(stored) != local)
        {
            defaultVersion = await PublishAsync(name, local, cancellationToken);
            stored = await ReadSkillMdAsync(name, defaultVersion, cancellationToken) ?? local;
            published = true;
        }

        var (description, body) = Split(stored);
        return new SkillVersion(name, defaultVersion!, description, body, published);
    }

    private async Task<string?> GetDefaultVersionAsync(string name, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"skills/{name}?api-version=v1", null, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, cancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return json.RootElement.TryGetProperty("default_version", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    // The content of a version is a ZIP with the SKILL.md in it.
    private async Task<string?> ReadSkillMdAsync(string name, string version, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"skills/{name}/versions/{version}/content?api-version=v1", null, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, cancellationToken);
        await using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync(cancellationToken), ZipArchiveMode.Read);
        var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase));
        if (entry is null) return null;
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private async Task<string> PublishAsync(string name, string skillMd, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(skillMd));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");
        form.Add(file, "files", "SKILL.md");
        using var created = await SendAsync(HttpMethod.Post, $"skills/{name}/versions?api-version=v1", form, cancellationToken);
        await EnsureSuccessAsync(created, cancellationToken);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync(cancellationToken));
        var version = json.RootElement.GetProperty("version").GetString()!;

        // The first version becomes the default by itself; later ones have to be promoted.
        using var promote = await SendAsync(HttpMethod.Post, $"skills/{name}?api-version=v1",
            new StringContent(JsonSerializer.Serialize(new { default_version = version }), Encoding.UTF8, "application/json"), cancellationToken);
        await EnsureSuccessAsync(promote, cancellationToken);
        return version;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relative, HttpContent? content, CancellationToken cancellationToken)
    {
        var token = await credential.GetTokenAsync(new TokenRequestContext(Scope), cancellationToken);
        using var request = new HttpRequestMessage(method, new Uri($"{projectEndpoint.ToString().TrimEnd('/')}/{relative}")) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Headers.Add("Foundry-Features", "Skills=V1Preview");
        return await http.SendAsync(request, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        throw new InvalidOperationException($"Foundry Skills API answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Trim();

    /// <summary>Splits a SKILL.md into the description of its front matter and the body that becomes instructions.</summary>
    public static (string Description, string Body) Split(string skillMd)
    {
        var lines = Normalize(skillMd).Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---") return ("", string.Join('\n', lines));
        var end = Array.FindIndex(lines, 1, l => l.Trim() == "---");
        if (end < 0) return ("", string.Join('\n', lines));
        var description = lines[1..end].Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("description:", StringComparison.Ordinal))?["description:".Length..].Trim() ?? "";
        return (description, string.Join('\n', lines[(end + 1)..]).Trim());
    }

    /// <summary>Finds evides-leakops/skills by walking up from the app folder, like the seeder does for the data.</summary>
    public static string FindDirectory(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath)) return Path.GetFullPath(explicitPath);
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "skills");
            if (Directory.Exists(candidate) && Directory.EnumerateFiles(candidate, "SKILL.md", SearchOption.AllDirectories).Any()) return candidate;
        }
        throw new DirectoryNotFoundException("Could not find the evides-leakops/skills folder. Set SKILLS_DIR.");
    }
}
