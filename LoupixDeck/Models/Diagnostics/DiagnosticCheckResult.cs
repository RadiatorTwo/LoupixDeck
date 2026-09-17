namespace LoupixDeck.Models.Diagnostics;

/// <summary>
/// The result of one diagnostic check. Immutable: a check builds it once and the UI and the
/// report only read it. <see cref="TechnicalDetail"/> and <see cref="Fix"/> may be null.
/// </summary>
public sealed record DiagnosticCheckResult
{
    /// <summary>Stable identifier of the check, for example "uinput.write-access".</summary>
    public required string Id { get; init; }

    /// <summary>The category the check is grouped under.</summary>
    public required DiagnosticCategory Category { get; init; }

    /// <summary>The outcome.</summary>
    public required DiagnosticStatus Status { get; init; }

    /// <summary>Localized title of the check.</summary>
    public required string Title { get; init; }

    /// <summary>Localized one-line result, written for a user, not for a developer.</summary>
    public required string Summary { get; init; }

    /// <summary>Raw technical output such as an errno line. May be null. Collapsed by default.</summary>
    public string TechnicalDetail { get; init; }

    /// <summary>The suggested solution, or null when there is nothing to suggest.</summary>
    public DiagnosticFix Fix { get; init; }

    /// <summary>
    /// Short key/value facts that back the result up. Everything in here reaches the copied
    /// report, so it must never carry a user name, a home directory or a full serial number.
    /// </summary>
    public IReadOnlyDictionary<string, string> Evidence { get; init; } =
        new Dictionary<string, string>(0);

    /// <summary>Builds a <see cref="DiagnosticStatus.Pass"/> result.</summary>
    public static DiagnosticCheckResult Pass(string id, DiagnosticCategory category, string title,
        string summary, string technicalDetail = null,
        IReadOnlyDictionary<string, string> evidence = null)
        => Create(id, category, DiagnosticStatus.Pass, title, summary, technicalDetail, null, evidence);

    /// <summary>Builds a <see cref="DiagnosticStatus.Warning"/> result.</summary>
    public static DiagnosticCheckResult Warning(string id, DiagnosticCategory category, string title,
        string summary, string technicalDetail = null, DiagnosticFix fix = null,
        IReadOnlyDictionary<string, string> evidence = null)
        => Create(id, category, DiagnosticStatus.Warning, title, summary, technicalDetail, fix, evidence);

    /// <summary>Builds a <see cref="DiagnosticStatus.Fail"/> result.</summary>
    public static DiagnosticCheckResult Fail(string id, DiagnosticCategory category, string title,
        string summary, string technicalDetail = null, DiagnosticFix fix = null,
        IReadOnlyDictionary<string, string> evidence = null)
        => Create(id, category, DiagnosticStatus.Fail, title, summary, technicalDetail, fix, evidence);

    /// <summary>Builds a <see cref="DiagnosticStatus.Skipped"/> result.</summary>
    public static DiagnosticCheckResult Skipped(string id, DiagnosticCategory category, string title,
        string summary, string technicalDetail = null)
        => Create(id, category, DiagnosticStatus.Skipped, title, summary, technicalDetail, null, null);

    /// <summary>Builds a <see cref="DiagnosticStatus.Unknown"/> result.</summary>
    public static DiagnosticCheckResult Unknown(string id, DiagnosticCategory category, string title,
        string summary, string technicalDetail = null)
        => Create(id, category, DiagnosticStatus.Unknown, title, summary, technicalDetail, null, null);

    private static DiagnosticCheckResult Create(string id, DiagnosticCategory category,
        DiagnosticStatus status, string title, string summary, string technicalDetail,
        DiagnosticFix fix, IReadOnlyDictionary<string, string> evidence)
    {
        return new DiagnosticCheckResult
        {
            Id = id,
            Category = category,
            Status = status,
            Title = title,
            Summary = summary,
            TechnicalDetail = technicalDetail,
            Fix = fix,
            Evidence = evidence ?? new Dictionary<string, string>(0)
        };
    }
}
