using GO2.Api.Models;

namespace GO2.Api.Contracts;

// Параметры старта оцифровки (опционально можно выбрать конкретную версию карты).
public sealed class StartDigitizationRequest
{
    public Guid? VersionId { get; set; }
}

// Ответ на старт оцифровки: id job и стартовый статус.
public sealed class StartDigitizationResponse
{
    public Guid JobId { get; set; }
    public DigitizationJobStatus Status { get; set; }
}

// Статус выполнения job для polling на фронтенде.
public sealed class DigitizationJobStatusResponse
{
    public Guid JobId { get; set; }
    public DigitizationJobStatus Status { get; set; }
    public int Progress { get; set; }
    public string Error { get; set; } = string.Empty;
    public decimal? MacroF1 { get; set; }
    public decimal? IoU { get; set; }
    public Guid MapVersionId { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
}
