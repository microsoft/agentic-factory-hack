using System.Net;
using System.Net.Http.Json;
using LeakOps.Contracts;
using LeakOps.Contracts.Logic;
using LeakOps.Seeder;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LeakOps.Tests;

public class CommunicationsTests
{
    private static readonly SeedData Data = SeedDataLoader.Load(SeedDataLoader.FindDataDirectory());
    private static readonly Scenario Burst = Data.Scenarios.Single(s => s.Id == "scn-burst-cast-iron");

    /// <summary>A diagnostic report for a burst on the given segment, built the same way the Diagnostic agent builds it.</summary>
    private static DiagnosticReport ReportFor(string segmentId)
    {
        var segment = Data.PipeSegments.Single(s => s.Id == segmentId);
        var valves = Data.Valves.Where(v => v.SegmentId == segmentId)
            .Select(v => new IsolationValveInfo(v.Id, v.Type, v.Status, v.TurnsToClose, v.LastOperatedUtc, v.Location, v.ChainageM)).ToList();
        var triage = new TriageVerdict(Burst.Alarm.AlarmId, segment.DistrictId, Burst.Alarm.SensorId, Burst.Alarm.TriggeredAtUtc,
            "genuine_leak", 0.97, "Leak.", [], segmentId, "diagnose");
        var judgement = new DiagnosisJudgement(segmentId, segment.LengthM / 3, 30, "burst", valves.Select(v => v.Id).ToList(), "Burst.",
            [new EvidenceItem("estimate-leak-position", "PS-101 fell 2.01 bar.", "leak")]);
        var procedure = Data.RepairProcedures.FirstOrDefault(p => p.Material == segment.Material && segment.DiameterMm >= p.MinDiameterMm
                                                                   && segment.DiameterMm <= p.MaxDiameterMm && p.BreachType == "burst");
        return DiagnosticRules.Build(judgement, segment, valves, procedure, triage, [new SegmentDrop(segmentId, 2.0)]);
    }

    private static DiagnosticReport BurstReport => ReportFor("SEG-0412");

    [Fact]
    public void Incident_ids_follow_the_alarm_id_and_are_always_valid_for_the_site()
    {
        Assert.Equal("INC-2026-1114-0001", IncidentIds.For("ALM-2026-1114-0001"));
        Assert.Equal("INC-weird-id", IncidentIds.For(" weird id "));
        Assert.Equal(64, IncidentIds.For(new string('x', 200)).Length);
        Assert.Matches("^[A-Za-z0-9._-]{1,64}$", IncidentIds.For("ALM/with spaces & symbols"));
    }

    [Fact]
    public void The_safe_template_passes_every_rule_for_every_segment()
    {
        // The template is what residents get when the model fails twice, so it must never be rejected itself.
        foreach (var segment in Data.PipeSegments)
        {
            var report = ReportFor(segment.Id);
            var issues = CommunicationsRules.Check(CommunicationsRules.Fallback(report), report);
            Assert.True(issues.Count == 0, $"{segment.Id}: {string.Join(" | ", issues)}");
        }
    }

    [Theory]
    [InlineData("Er is een probleem met segment SEG-0412.", "internal id")]
    [InlineData("Schuif V-0412-B wordt gesloten.", "valve or technician id")]
    [InlineData("Dit is een S1 storing.", "severity code")]
    [InlineData("Het gaat om een DN300 leiding.", "pipe diameter")]
    [InlineData("De oude gietijzeren leiding is gebarsten.", "pipe material")]
    [InlineData("De reparatie kost ongeveer € 5000.", "cost")]
    [InlineData("De storing is binnen 4 uur verholpen.", "time or duration")]
    [InlineData("Rond 14:30 is het water terug.", "time or duration")]
    [InlineData("Dit is de schuld van de aannemer.", "blame")]
    [InlineData("Zorgcentrum De Hoven krijgt voorrang.", "names a specific customer")]
    [InlineData("Zie het draaiboek【21:2†source】.", "citation mark")]
    public void Drafts_with_internal_details_promises_or_blame_are_rejected(string sentence, string expectedIssue)
    {
        var report = BurstReport;
        var good = CommunicationsRules.Fallback(report);

        var issues = CommunicationsRules.Check(good with { MessageNl = good.MessageNl + " " + sentence }, report);

        Assert.Contains(issues, i => i.Contains(expectedIssue));
    }

    [Fact]
    public void Drafts_must_be_complete_short_enough_located_and_translated()
    {
        var report = BurstReport;
        var good = CommunicationsRules.Fallback(report);

        Assert.Contains(CommunicationsRules.Check(good with { AdviceNl = new string('a', 400) }, report), i => i.Contains("adviceNl is too long"));
        Assert.Empty(CommunicationsRules.Check(good with { AdviceNl = "", AdviceEn = "" }, report));   // the advice is not the model's job: empty is fine
        Assert.Contains(CommunicationsRules.Check(good with { MessageEn = new string('a', 600) }, report), i => i.Contains("messageEn is too long"));
        Assert.Contains(CommunicationsRules.Check(good with { TitleNl = "Storing", MessageNl = "Er is een storing aan de waterleiding." }, report),
            i => i.Contains("Dutch title and message name neither"));
        Assert.Contains(CommunicationsRules.Check(good with { MessageEn = good.MessageNl }, report), i => i.Contains("copy of the Dutch"));
    }

    [Theory]
    [InlineData("Het water is veilig om te drinken.")]
    [InlineData("Drink het water niet zonder het te koken.")]
    [InlineData("Boil the water before use.")]
    [InlineData("The water is safe to drink.")]
    [InlineData("Het leidingwater is besmet met bacteriën.")]
    [InlineData("Water that is contaminated may cause disease.")]
    [InlineData("There is a risk of flooding in the area.")]
    public void Health_and_safety_claims_about_the_water_are_never_published(string sentence)
    {
        var report = BurstReport;
        var good = CommunicationsRules.Fallback(report);

        var issues = CommunicationsRules.Check(good with { MessageNl = good.MessageNl + " " + sentence, MessageEn = good.MessageEn + " " + sentence }, report);

        Assert.Contains(issues, i => i.Contains("health or safety claim"));
    }

    [Fact]
    public void Ordinary_words_like_drinkwater_are_not_mistaken_for_health_advice()
    {
        var report = BurstReport;
        var good = CommunicationsRules.Fallback(report);

        var issues = CommunicationsRules.Check(good with { TitleNl = "Storing in de drinkwatervoorziening Spijkenisse", TitleEn = "Disruption of the drinking water supply in Spijkenisse" }, report);

        Assert.Empty(issues);
    }

    [Fact]
    public async Task The_advice_residents_get_is_always_the_standard_advice_whatever_the_model_wrote()
    {
        var report = BurstReport;
        var draft = CommunicationsRules.Fallback(report) with { TitleNl = "Geen water in delen van Spijkenisse", AdviceNl = "Gebruik het water gewoon.", AdviceEn = "Anything goes." };

        var decision = await CommunicationsRules.ChooseNoticeAsync(report, _ => Task.FromResult(draft));

        Assert.False(decision.UsedFallback);
        Assert.Equal(CommunicationsRules.Fallback(report).AdviceNl, decision.Notice.AdviceNl);
        Assert.Equal(CommunicationsRules.Fallback(report).AdviceEn, decision.Notice.AdviceEn);
        Assert.Contains(CommunicationsRules.HelpLine, decision.Notice.AdviceNl);
    }

    [Theory]
    [InlineData("ALM-2026-1114-0001", true)]
    [InlineData("ALM-A", true)]
    [InlineData("A", false)]                        // would collide with ALM-A: both would become INC-A
    [InlineData("ALM-A/B", false)]                  // sanitising would turn it into the same id as ALM-A-B
    [InlineData("ALM-A B", false)]
    [InlineData("alm-lowercase", false)]
    [InlineData("ALM-", false)]
    [InlineData("", false)]
    public void Only_alarm_ids_that_cannot_collide_are_accepted(string alarmId, bool valid)
    {
        Assert.Equal(valid, IncidentIds.IsValidAlarmId(alarmId));
        Assert.False(IncidentIds.IsValidAlarmId(new string('x', 70)));
    }

    [Fact]
    public async Task A_good_first_draft_is_used_as_is()
    {
        var report = BurstReport;
        var good = CommunicationsRules.Fallback(report) with { TitleNl = "Geen water in delen van Spijkenisse" };
        var calls = 0;

        var decision = await CommunicationsRules.ChooseNoticeAsync(report, _ => { calls++; return Task.FromResult(good); });

        Assert.Equal(good, decision.Notice);
        Assert.False(decision.UsedFallback);
        Assert.Empty(decision.RejectedIssues);
        Assert.Equal((1, 1), (calls, decision.Attempts));
    }

    [Fact]
    public async Task A_rejected_draft_is_retried_with_the_reasons_as_feedback()
    {
        var report = BurstReport;
        var good = CommunicationsRules.Fallback(report);
        var bad = good with { MessageNl = good.MessageNl + " De storing is binnen 4 uur verholpen." };
        var feedbackSeen = new List<string>();

        var decision = await CommunicationsRules.ChooseNoticeAsync(report, feedback =>
        {
            feedbackSeen.Add(feedback);
            return Task.FromResult(feedbackSeen.Count == 1 ? bad : good);
        });

        Assert.Equal(good, decision.Notice);
        Assert.False(decision.UsedFallback);
        Assert.Equal(2, decision.Attempts);
        Assert.Equal("", feedbackSeen[0]);                                   // first attempt has no feedback
        Assert.Contains("time or duration", feedbackSeen[1]);                // second attempt is told why the first failed
        Assert.Contains(decision.RejectedIssues, i => i.Contains("time or duration"));
    }

    [Fact]
    public async Task When_every_draft_is_rejected_the_safe_template_is_published()
    {
        var report = BurstReport;
        var bad = CommunicationsRules.Fallback(report) with { MessageNl = "Dit is de schuld van de aannemer, segment SEG-0412 is stuk." };
        var calls = 0;

        var decision = await CommunicationsRules.ChooseNoticeAsync(report, _ => { calls++; return Task.FromResult(bad); });

        Assert.True(decision.UsedFallback);
        Assert.Equal(2, calls);
        Assert.Equal(CommunicationsRules.Fallback(report), decision.Notice);
        Assert.Empty(CommunicationsRules.Check(decision.Notice, report));   // what residents get is always valid
        Assert.Contains(decision.RejectedIssues, i => i.Contains("blame"));
        Assert.Contains(decision.RejectedIssues, i => i.Contains("internal id"));
    }

    [Fact]
    public void The_incident_payload_gets_every_fact_from_the_diagnosis_and_has_no_repair_time_yet()
    {
        var report = BurstReport;

        var request = CommunicationsRules.BuildRequest(CommunicationsRules.Fallback(report), report);

        Assert.Equal("INC-2026-1114-0001", request.IncidentId);
        Assert.Equal(WaterstoringStatus.Reported, request.Status);
        Assert.Equal("Spijkenisse", request.Place);
        Assert.Equal(["Dorpsweg", "Kerkstraat", "Havenlaan"], request.AffectedStreets);
        Assert.Equal(report.PostalCodes, request.AffectedPostalCodes);
        Assert.Equal(1840, request.CustomersAffected);
        Assert.Equal(report.Location, request.Location);
        Assert.Equal(2, request.AffectedPath!.Count);
        Assert.Null(request.ExpectedResolutionUtc);   // unknown until the Scheduling agent plans the work
    }

    [Fact]
    public async Task The_payload_is_accepted_by_the_waterstoring_site_and_residents_can_find_it()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "dev-waterstoring-key");
        var report = BurstReport;
        var request = CommunicationsRules.BuildRequest(CommunicationsRules.Fallback(report), report);

        var first = await client.PostAsJsonAsync("/api/incidents", request);
        var again = await client.PostAsJsonAsync("/api/incidents", request);   // a retry must be harmless
        var byPostcode = await client.GetFromJsonAsync<SearchResult>("/api/search?q=3201%20ab");
        var byStreet = await client.GetFromJsonAsync<SearchResult>("/api/search?q=Havenlaan");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Single((await client.GetFromJsonAsync<List<WaterstoringIncident>>("/api/incidents"))!);
        Assert.True(byPostcode!.HasIncident);
        Assert.True(byStreet!.HasIncident);
    }

    private record SearchResult(string? Query, bool HasIncident, List<WaterstoringIncident> Incidents);
}
