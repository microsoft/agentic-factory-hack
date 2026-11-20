namespace LeakOps.Contracts.Logic;

public record SeverityAssessment(string Severity, IReadOnlyList<string> Reasons);

/// <summary>
/// One fixed rubric for how serious a breach is, so every diagnosis is classified the same way and the reasons can be shown.
/// S1 critical: a burst that affects critical customers (care home, hospital), 1000 or more customers, or a trunk main (DN400 or more).
/// S2 major: another burst on DN200 or more or with 100 or more customers; or a leak that affects critical customers or 1000+ customers.
/// S3 moderate: any other burst; a leak that affects 100 or more customers.
/// S4 minor: a leak that affects fewer than 100 customers.
/// </summary>
public static class SeverityRubric
{
    public static SeverityAssessment Classify(string breachType, int customersServed, IReadOnlyList<string> criticalCustomers, int diameterMm)
    {
        var burst = breachType == "burst";
        var critical = criticalCustomers.Count > 0;
        var reasons = new List<string> { burst ? "burst (pipe broken open)" : "leak (water escaping, pipe still intact)" };
        if (critical) reasons.Add($"critical customers affected: {string.Join(", ", criticalCustomers)}");
        reasons.Add($"{customersServed} customers served by the segment");
        reasons.Add($"DN{diameterMm} pipe");

        var severity = (burst, critical, customersServed, diameterMm) switch
        {
            (true, true, _, _) => "S1",
            (true, _, >= 1000, _) => "S1",
            (true, _, _, >= 400) => "S1",
            (true, _, >= 100, _) => "S2",
            (true, _, _, >= 200) => "S2",
            (true, _, _, _) => "S3",
            (false, true, _, _) => "S2",
            (false, _, >= 1000, _) => "S2",
            (false, _, >= 100, _) => "S3",
            _ => "S4",
        };
        return new SeverityAssessment(severity, reasons);
    }
}
