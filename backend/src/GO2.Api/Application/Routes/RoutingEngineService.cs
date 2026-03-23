using GO2.Api.Contracts;
using GO2.Api.Models;

namespace GO2.Api.Application.Routes;

// Базовый движок маршрутизации: строит граф по сетке, считает top-3 и explainability.
public sealed class RoutingEngineService
{
    private const int GridWidth = 40;
    private const int GridHeight = 24;
    private const double CanvasWidth = 900;
    private const double CanvasHeight = 560;

    public RouteCalculationResultDto Calculate(
        IReadOnlyCollection<TerrainObject> objects,
        IReadOnlyList<RoutePointDto> waypoints,
        RouteProfileDto profile)
    {
        var grid = BuildGrid(objects, profile);
        var variants = BuildTop3(grid, waypoints, profile);
        return new RouteCalculationResultDto
        {
            Routes = variants,
            Summary = variants.Count == 0
                ? "Маршруты не найдены для заданных точек."
                : $"Найдено {variants.Count} маршрут(а). Лучший вариант: №{variants[0].Rank}."
        };
    }

    private static double[,] BuildGrid(IReadOnlyCollection<TerrainObject> objects, RouteProfileDto profile)
    {
        var weights = new double[GridWidth, GridHeight];
        for (var x = 0; x < GridWidth; x++)
        {
            for (var y = 0; y < GridHeight; y++)
            {
                weights[x, y] = 1.0;
            }
        }

        foreach (var obj in objects)
        {
            var points = ParsePoints(obj);
            if (points.Count == 0)
            {
                continue;
            }

            var traversalPenalty = obj.Traversability <= 0.2m
                ? 6.0
                : Math.Max(0.2, 2.0 - (double)obj.Traversability);
            var riskPenalty = obj.TerrainClass is TerrainClass.Water or TerrainClass.Rock ? 1.6 : 1.0;
            var blendedPenalty = profile.TimeWeight * traversalPenalty + profile.SafetyWeight * riskPenalty;

            foreach (var point in points)
            {
                var gx = Math.Clamp((int)Math.Round(point.X / CanvasWidth * (GridWidth - 1)), 0, GridWidth - 1);
                var gy = Math.Clamp((int)Math.Round(point.Y / CanvasHeight * (GridHeight - 1)), 0, GridHeight - 1);
                weights[gx, gy] = Math.Max(weights[gx, gy], blendedPenalty);
            }
        }

        return weights;
    }

    private List<RouteVariantDto> BuildTop3(double[,] grid, IReadOnlyList<RoutePointDto> waypoints, RouteProfileDto profile)
    {
        if (waypoints.Count < 2)
        {
            return [];
        }

        var result = new List<RouteVariantDto>();
        var penalties = new double[GridWidth, GridHeight];

        for (var i = 0; i < 5 && result.Count < 3; i++)
        {
            var path = BuildPathForWaypoints(grid, penalties, waypoints);
            if (path.Count == 0)
            {
                break;
            }

            var candidate = BuildVariant(path, grid, result.Count + 1, profile);
            var hasHighOverlap = result.Any(existing => Overlap(existing.Polyline, candidate.Polyline) > 0.7);
            if (!hasHighOverlap)
            {
                result.Add(candidate);
            }

            // Штрафуем использованные клетки, чтобы получить разнообразные альтернативы.
            foreach (var point in path)
            {
                penalties[point.X, point.Y] += 1.4 + i * 0.4;
            }
        }

        return result.OrderBy(x => x.TotalCost).Select((x, index) =>
        {
            x.Rank = index + 1;
            return x;
        }).ToList();
    }

    private static List<GridPoint> BuildPathForWaypoints(
        double[,] weights,
        double[,] penalties,
        IReadOnlyList<RoutePointDto> waypoints)
    {
        var all = new List<GridPoint>();
        for (var i = 0; i < waypoints.Count - 1; i++)
        {
            var start = ToGrid(waypoints[i]);
            var end = ToGrid(waypoints[i + 1]);
            var segment = AStar(weights, penalties, start, end);
            if (segment.Count == 0)
            {
                return [];
            }

            if (all.Count > 0 && segment.Count > 0)
            {
                segment.RemoveAt(0);
            }

            all.AddRange(segment);
        }

        return all;
    }

    private static List<GridPoint> AStar(double[,] weights, double[,] penalties, GridPoint start, GridPoint end)
    {
        var open = new PriorityQueue<GridPoint, double>();
        var cameFrom = new Dictionary<GridPoint, GridPoint>();
        var g = new Dictionary<GridPoint, double> { [start] = 0 };
        open.Enqueue(start, 0);

        while (open.Count > 0)
        {
            var current = open.Dequeue();
            if (current.Equals(end))
            {
                return Reconstruct(cameFrom, current);
            }

            foreach (var next in Neighbors(current))
            {
                if (next.X < 0 || next.Y < 0 || next.X >= GridWidth || next.Y >= GridHeight)
                {
                    continue;
                }

                var stepCost = weights[next.X, next.Y] + penalties[next.X, next.Y];
                var tentative = g[current] + stepCost;
                if (!g.TryGetValue(next, out var known) || tentative < known)
                {
                    cameFrom[next] = current;
                    g[next] = tentative;
                    var f = tentative + Heuristic(next, end);
                    open.Enqueue(next, f);
                }
            }
        }

        return [];
    }

    private static List<GridPoint> Reconstruct(Dictionary<GridPoint, GridPoint> cameFrom, GridPoint current)
    {
        var result = new List<GridPoint> { current };
        while (cameFrom.TryGetValue(current, out var previous))
        {
            result.Add(previous);
            current = previous;
        }

        result.Reverse();
        return result;
    }

    private static IEnumerable<GridPoint> Neighbors(GridPoint point)
    {
        yield return new GridPoint(point.X + 1, point.Y);
        yield return new GridPoint(point.X - 1, point.Y);
        yield return new GridPoint(point.X, point.Y + 1);
        yield return new GridPoint(point.X, point.Y - 1);
    }

    private static double Heuristic(GridPoint a, GridPoint b)
    {
        return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    }

    private static GridPoint ToGrid(RoutePointDto point)
    {
        return new GridPoint(
            Math.Clamp((int)Math.Round(point.X / CanvasWidth * (GridWidth - 1)), 0, GridWidth - 1),
            Math.Clamp((int)Math.Round(point.Y / CanvasHeight * (GridHeight - 1)), 0, GridHeight - 1));
    }

    private static RoutePointDto ToCanvas(GridPoint point)
    {
        return new RoutePointDto
        {
            X = point.X / (double)(GridWidth - 1) * CanvasWidth,
            Y = point.Y / (double)(GridHeight - 1) * CanvasHeight
        };
    }

    private static RouteVariantDto BuildVariant(List<GridPoint> path, double[,] grid, int rank, RouteProfileDto profile)
    {
        var points = path.Select(ToCanvas).ToList();
        var segments = new List<RouteSegmentDto>();
        double totalCost = 0;
        double risk = 0;
        for (var i = 1; i < path.Count; i++)
        {
            var from = path[i - 1];
            var to = path[i];
            var cellCost = grid[to.X, to.Y];
            var segmentRisk = cellCost > 1.6 ? 0.9 : cellCost > 1.2 ? 0.6 : 0.3;
            totalCost += cellCost;
            risk += segmentRisk;
            segments.Add(new RouteSegmentDto
            {
                From = ToCanvas(from),
                To = ToCanvas(to),
                SegmentCost = Math.Round(cellCost, 3),
                SegmentRisk = Math.Round(segmentRisk, 3)
            });
        }

        var length = Math.Max(1.0, path.Count * 12.0);
        var estimatedTime = length * (0.7 + profile.TimeWeight);
        var penalty = totalCost * profile.SafetyWeight;

        return new RouteVariantDto
        {
            Rank = rank,
            TotalCost = Math.Round(totalCost, 3),
            Length = Math.Round(length, 2),
            EstimatedTime = Math.Round(estimatedTime, 2),
            RiskScore = Math.Round(risk / Math.Max(1, segments.Count), 3),
            PenaltyScore = Math.Round(penalty, 3),
            Polyline = points,
            Segments = segments,
            WhyChosen =
            [
                $"Баланс времени/безопасности: {profile.TimeWeight:0.##}/{profile.SafetyWeight:0.##}.",
                $"Длина: {Math.Round(length, 2)} м, стоимость: {Math.Round(totalCost, 2)}.",
                "Альтернатива отфильтрована по порогу overlap 70%."
            ]
        };
    }

    private static double Overlap(IReadOnlyCollection<RoutePointDto> first, IReadOnlyCollection<RoutePointDto> second)
    {
        if (first.Count == 0 || second.Count == 0)
        {
            return 1.0;
        }

        var firstSet = first.Select(x => $"{Math.Round(x.X, 0)}:{Math.Round(x.Y, 0)}").ToHashSet();
        var secondSet = second.Select(x => $"{Math.Round(x.X, 0)}:{Math.Round(x.Y, 0)}").ToHashSet();
        var intersection = firstSet.Intersect(secondSet).Count();
        var union = firstSet.Union(secondSet).Count();
        return union == 0 ? 1.0 : intersection / (double)union;
    }

    private static List<RoutePointDto> ParsePoints(TerrainObject obj)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(obj.GeometryJson);
            if (obj.GeometryKind == TerrainGeometryKind.Point)
            {
                var x = doc.RootElement.GetProperty("x").GetDouble();
                var y = doc.RootElement.GetProperty("y").GetDouble();
                return [new RoutePointDto { X = x, Y = y }];
            }

            var list = new List<RoutePointDto>();
            foreach (var item in doc.RootElement.GetProperty("points").EnumerateArray())
            {
                list.Add(new RoutePointDto
                {
                    X = item.GetProperty("x").GetDouble(),
                    Y = item.GetProperty("y").GetDouble()
                });
            }

            return list;
        }
        catch
        {
            return [];
        }
    }

    private readonly record struct GridPoint(int X, int Y);
}
