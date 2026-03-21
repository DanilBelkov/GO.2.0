namespace GO2.Api.Services;

// Контракт расчета базовых метрик качества оцифровки.
public interface IDigitizationQualityService
{
    decimal ComputeMacroF1(IReadOnlyCollection<int> expectedByClass, IReadOnlyCollection<int> predictedByClass);
    decimal ComputeIoU(int expectedArea, int predictedArea, int intersectionArea);
}

// Упрощенный сервис качества (Macro F1 и IoU) для MVP-отчетности.
public sealed class DigitizationQualityService : IDigitizationQualityService
{
    public decimal ComputeMacroF1(IReadOnlyCollection<int> expectedByClass, IReadOnlyCollection<int> predictedByClass)
    {
        // Защита от некорректного входа: считаем, что качество неизвестно.
        if (expectedByClass.Count == 0 || predictedByClass.Count == 0 || expectedByClass.Count != predictedByClass.Count)
        {
            return 0m;
        }

        var expected = expectedByClass.ToArray();
        var predicted = predictedByClass.ToArray();
        decimal sum = 0m;
        for (var i = 0; i < expected.Length; i++)
        {
            var tp = Math.Min(expected[i], predicted[i]);
            var fp = Math.Max(0, predicted[i] - tp);
            var fn = Math.Max(0, expected[i] - tp);
            var precision = tp == 0 ? 0 : (decimal)tp / (tp + fp);
            var recall = tp == 0 ? 0 : (decimal)tp / (tp + fn);
            var f1 = (precision + recall) == 0 ? 0 : 2 * precision * recall / (precision + recall);
            sum += f1;
        }

        return decimal.Round(sum / expected.Length, 4);
    }

    public decimal ComputeIoU(int expectedArea, int predictedArea, int intersectionArea)
    {
        var union = expectedArea + predictedArea - intersectionArea;
        if (union <= 0)
        {
            return 0m;
        }

        return decimal.Round((decimal)intersectionArea / union, 4);
    }
}
