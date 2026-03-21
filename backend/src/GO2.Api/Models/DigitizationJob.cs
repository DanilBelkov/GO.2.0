namespace GO2.Api.Models;

// Запись фонового задания оцифровки, которую фронтенд опрашивает через polling.
public sealed class DigitizationJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MapId { get; set; }
    public Map Map { get; set; } = null!;
    public Guid MapVersionId { get; set; }
    public MapVersion MapVersion { get; set; } = null!;
    public Guid OwnerUserId { get; set; }
    public DigitizationJobStatus Status { get; set; } = DigitizationJobStatus.Queued;
    public int Progress { get; set; }
    public string Error { get; set; } = string.Empty;
    public decimal? MacroF1 { get; set; }
    public decimal? IoU { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
}
