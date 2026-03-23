namespace GO2.Api.Contracts;

// Точка маршрута на карте.
public sealed class RoutePointDto
{
    public double X { get; set; }
    public double Y { get; set; }
}

// Профиль пользователя для балансировки времени/безопасности.
public sealed class RouteProfileDto
{
    public double TimeWeight { get; set; } = 0.6;
    public double SafetyWeight { get; set; } = 0.4;
}

// Запрос запуска расчета маршрутов.
public sealed class CalculateRoutesRequest
{
    public Guid? MapVersionId { get; set; }
    public List<RoutePointDto> Waypoints { get; set; } = [];
    public RouteProfileDto Profile { get; set; } = new();
}

// Ответ старта асинхронного route job.
public sealed class CalculateRoutesResponse
{
    public Guid JobId { get; set; }
    public string Status { get; set; } = "in-progress";
}

// Сегмент маршрута с метриками риска/стоимости для визуализации.
public sealed class RouteSegmentDto
{
    public RoutePointDto From { get; set; } = new();
    public RoutePointDto To { get; set; } = new();
    public double SegmentCost { get; set; }
    public double SegmentRisk { get; set; }
}

// Один вариант маршрута из top-3.
public sealed class RouteVariantDto
{
    public int Rank { get; set; }
    public double TotalCost { get; set; }
    public double Length { get; set; }
    public double EstimatedTime { get; set; }
    public double RiskScore { get; set; }
    public double PenaltyScore { get; set; }
    public List<RoutePointDto> Polyline { get; set; } = [];
    public List<RouteSegmentDto> Segments { get; set; } = [];
    public List<string> WhyChosen { get; set; } = [];
}

// Финальный payload расчета для UI сравнения маршрутов.
public sealed class RouteCalculationResultDto
{
    public List<RouteVariantDto> Routes { get; set; } = [];
    public string Summary { get; set; } = string.Empty;
}

// Polling-ответ по состоянию route job.
public sealed class RouteJobStatusResponse
{
    public Guid JobId { get; set; }
    public string Status { get; set; } = "in-progress";
    public int Progress { get; set; }
    public string Error { get; set; } = string.Empty;
    public RouteCalculationResultDto? Result { get; set; }
}
