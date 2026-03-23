using GO2.Api.Contracts;
using GO2.Api.Models;

namespace GO2.Api.Application.Routes;

// Базовый движок маршрутизации: строит взвешенный граф по сетке, считает top-3 и explainability.
public sealed class RoutingEngineService
{
    private const int GridWidth = 40;
    private const int GridHeight = 24;
    private const double CanvasWidth = 900;
    private const double CanvasHeight = 560;

    public RouteGraphResponse BuildGraph(IReadOnlyCollection<TerrainObject> objects, RouteProfileDto profile)
    {
        var graph = BuildGridGraph(objects, profile);
        var nodes = new List<RouteGraphNodeDto>();
        var edges = new List<RouteGraphEdgeDto>();

        for (var x = 0; x < GridWidth; x++)
        {
            for (var y = 0; y < GridHeight; y++)
            {
                if (graph.Blocked[x, y])
                {
                    continue;
                }

                var point = new GridPoint(x, y);
                nodes.Add(new RouteGraphNodeDto
                {
                    Id = ToNodeId(point),
                    X = ToCanvas(point).X,
                    Y = ToCanvas(point).Y
                });

                // Ребра "вправо/вниз", чтобы не дублировать визуализацию неориентированного графа.
                AddEdgeIfWalkable(graph, edges, point, new GridPoint(x + 1, y));
                AddEdgeIfWalkable(graph, edges, point, new GridPoint(x, y + 1));
            }
        }

        return new RouteGraphResponse
        {
            Nodes = nodes,
            Edges = edges,
            GridWidth = GridWidth,
            GridHeight = GridHeight,
            Summary = nodes.Count == 0
                ? "Не удалось построить граф: вся карта непроходима."
                : $"Построен граф: {nodes.Count} узлов, {edges.Count} ребер."
        };
    }

    public RouteCalculationResultDto Calculate(
        IReadOnlyCollection<TerrainObject> objects,
        IReadOnlyList<RoutePointDto> waypoints,
        RouteProfileDto profile)
    {
        var graph = BuildGridGraph(objects, profile);
        var variants = BuildTop3(graph, waypoints, profile);
        return new RouteCalculationResultDto
        {
            Routes = variants,
            Summary = variants.Count == 0
                ? "Маршруты не найдены для заданных точек."
                : $"Найдено {variants.Count} маршрут(а). Лучший вариант: №{variants[0].Rank}."
        };
    }

    private static void AddEdgeIfWalkable(GridGraph graph, List<RouteGraphEdgeDto> edges, GridPoint from, GridPoint to)
    {
        if (!IsInside(to) || graph.Blocked[to.X, to.Y] || graph.Blocked[from.X, from.Y])
        {
            return;
        }

        edges.Add(new RouteGraphEdgeDto
        {
            FromNodeId = ToNodeId(from),
            ToNodeId = ToNodeId(to),
            Weight = Math.Round((graph.Weights[from.X, from.Y] + graph.Weights[to.X, to.Y]) / 2.0, 3)
        });
    }

    private static GridGraph BuildGridGraph(IReadOnlyCollection<TerrainObject> objects, RouteProfileDto profile)
    {
        var weights = new double[GridWidth, GridHeight];
        var blocked = new bool[GridWidth, GridHeight];

        for (var x = 0; x < GridWidth; x++)
        {
            for (var y = 0; y < GridHeight; y++)
            {
                weights[x, y] = 1.0;
                blocked[x, y] = false;
            }
        }

        foreach (var obj in objects)
        {
            var affectedCells = GetAffectedCells(obj);
            if (affectedCells.Count == 0)
            {
                continue;
            }

            var traversability = (double)GetEffectiveTraversability(obj);
            var markBlocked = traversability <= 0.05;
            var terrainRisk = obj.TerrainClass switch
            {
                TerrainClass.Water => 2.2,
                TerrainClass.Rock => 1.7,
                TerrainClass.Vegetation => 1.25,
                TerrainClass.ManMade => 1.1,
                _ => 1.0
            };
            var movementPenalty = Math.Clamp(1.0 / Math.Max(0.05, traversability), 0.2, 10.0);
            var blendedPenalty = profile.TimeWeight * movementPenalty + profile.SafetyWeight * terrainRisk;

            foreach (var cell in affectedCells)
            {
                if (!IsInside(cell))
                {
                    continue;
                }

                if (markBlocked)
                {
                    blocked[cell.X, cell.Y] = true;
                    continue;
                }

                if (!blocked[cell.X, cell.Y])
                {
                    weights[cell.X, cell.Y] = Math.Max(weights[cell.X, cell.Y], blendedPenalty);
                }
            }
        }

        return new GridGraph(weights, blocked);
    }

    private List<RouteVariantDto> BuildTop3(GridGraph graph, IReadOnlyList<RoutePointDto> waypoints, RouteProfileDto profile)
    {
        if (waypoints.Count < 2)
        {
            return [];
        }

        var snapped = waypoints
            .Select(point => SnapToWalkableNode(graph, ToGrid, point))
            .ToList();

        if (snapped.Any(x => x is null))
        {
            return [];
        }

        var snappedWaypoints = snapped.Select(x => x!.Value).ToList();
        var result = new List<RouteVariantDto>();
        var penalties = new double[GridWidth, GridHeight];

        for (var i = 0; i < 6 && result.Count < 3; i++)
        {
            var path = BuildPathForWaypoints(graph, penalties, snappedWaypoints);
            if (path.Count == 0)
            {
                break;
            }

            var candidate = BuildVariant(path, graph, result.Count + 1, profile);
            var hasHighOverlap = result.Any(existing => Overlap(existing.Polyline, candidate.Polyline) > 0.7);
            if (!hasHighOverlap)
            {
                result.Add(candidate);
            }

            // Штрафуем использованные клетки, чтобы получить разнообразные альтернативы.
            foreach (var point in path)
            {
                penalties[point.X, point.Y] += 1.4 + i * 0.35;
            }
        }

        return result.OrderBy(x => x.TotalCost).Select((x, index) =>
        {
            x.Rank = index + 1;
            return x;
        }).ToList();
    }

    private static GridPoint? SnapToWalkableNode(GridGraph graph, Func<RoutePointDto, GridPoint> toGrid, RoutePointDto point)
    {
        var start = toGrid(point);
        if (IsInside(start) && !graph.Blocked[start.X, start.Y])
        {
            return start;
        }

        for (var radius = 1; radius <= Math.Max(GridWidth, GridHeight); radius++)
        {
            GridPoint? best = null;
            var bestDistance = double.MaxValue;

            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dy = -radius; dy <= radius; dy++)
                {
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                    {
                        continue;
                    }

                    var candidate = new GridPoint(start.X + dx, start.Y + dy);
                    if (!IsInside(candidate) || graph.Blocked[candidate.X, candidate.Y])
                    {
                        continue;
                    }

                    var distance = Math.Sqrt(dx * dx + dy * dy);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = candidate;
                    }
                }
            }

            if (best is not null)
            {
                return best;
            }
        }

        return null;
    }

    private static List<GridPoint> BuildPathForWaypoints(
        GridGraph graph,
        double[,] penalties,
        IReadOnlyList<GridPoint> waypoints)
    {
        var all = new List<GridPoint>();
        for (var i = 0; i < waypoints.Count - 1; i++)
        {
            var segment = AStar(graph, penalties, waypoints[i], waypoints[i + 1]);
            if (segment.Count == 0)
            {
                return [];
            }

            if (all.Count > 0)
            {
                segment.RemoveAt(0);
            }

            all.AddRange(segment);
        }

        return all;
    }

    private static List<GridPoint> AStar(GridGraph graph, double[,] penalties, GridPoint start, GridPoint end)
    {
        if (!IsInside(start) || !IsInside(end) || graph.Blocked[start.X, start.Y] || graph.Blocked[end.X, end.Y])
        {
            return [];
        }

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
                if (!IsInside(next) || graph.Blocked[next.X, next.Y])
                {
                    continue;
                }

                var baseWeight = (graph.Weights[current.X, current.Y] + graph.Weights[next.X, next.Y]) / 2.0;
                var stepCost = baseWeight + penalties[next.X, next.Y];
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

    private static bool IsInside(GridPoint point)
    {
        return point.X >= 0 && point.Y >= 0 && point.X < GridWidth && point.Y < GridHeight;
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

    private static string ToNodeId(GridPoint point)
    {
        return $"{point.X}:{point.Y}";
    }

    private static RouteVariantDto BuildVariant(List<GridPoint> path, GridGraph graph, int rank, RouteProfileDto profile)
    {
        var points = path.Select(ToCanvas).ToList();
        var segments = new List<RouteSegmentDto>();
        double totalCost = 0;
        double risk = 0;

        for (var i = 1; i < path.Count; i++)
        {
            var from = path[i - 1];
            var to = path[i];
            var cellCost = (graph.Weights[from.X, from.Y] + graph.Weights[to.X, to.Y]) / 2.0;
            var segmentRisk = cellCost > 2.0 ? 0.9 : cellCost > 1.4 ? 0.6 : 0.3;
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
                "Маршрут проходит по узлам построенного графа карты."
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

    private static decimal GetEffectiveTraversability(TerrainObject obj)
    {
        if (obj.TerrainObjectType is not null)
        {
            return obj.TerrainObjectType.Traversability;
        }

        return obj.Traversability;
    }

    private static HashSet<GridPoint> GetAffectedCells(TerrainObject obj)
    {
        var points = ParsePoints(obj);
        var cells = new HashSet<GridPoint>();
        if (points.Count == 0)
        {
            return cells;
        }

        if (obj.GeometryKind == TerrainGeometryKind.Point)
        {
            cells.Add(ToGrid(points[0]));
            return cells;
        }

        if (obj.GeometryKind == TerrainGeometryKind.Line)
        {
            AddPolylineCells(points, cells);
            return cells;
        }

        AddPolygonCells(points, cells);
        return cells;
    }

    private static void AddPolylineCells(IReadOnlyList<RoutePointDto> points, HashSet<GridPoint> cells)
    {
        for (var i = 1; i < points.Count; i++)
        {
            var from = points[i - 1];
            var to = points[i];
            var distance = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));
            var steps = Math.Max(1, (int)Math.Ceiling(distance / 8));

            for (var step = 0; step <= steps; step++)
            {
                var t = step / (double)steps;
                var interpolated = new RoutePointDto
                {
                    X = from.X + (to.X - from.X) * t,
                    Y = from.Y + (to.Y - from.Y) * t
                };
                cells.Add(ToGrid(interpolated));
            }
        }
    }

    private static void AddPolygonCells(IReadOnlyList<RoutePointDto> points, HashSet<GridPoint> cells)
    {
        if (points.Count < 3)
        {
            return;
        }

        AddPolylineCells(points.Concat([points[0]]).ToList(), cells);

        var minX = points.Min(p => p.X);
        var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxY = points.Max(p => p.Y);

        var minGrid = ToGrid(new RoutePointDto { X = minX, Y = minY });
        var maxGrid = ToGrid(new RoutePointDto { X = maxX, Y = maxY });

        for (var gx = Math.Min(minGrid.X, maxGrid.X); gx <= Math.Max(minGrid.X, maxGrid.X); gx++)
        {
            for (var gy = Math.Min(minGrid.Y, maxGrid.Y); gy <= Math.Max(minGrid.Y, maxGrid.Y); gy++)
            {
                var center = ToCanvas(new GridPoint(gx, gy));
                if (PointInPolygon(center, points))
                {
                    cells.Add(new GridPoint(gx, gy));
                }
            }
        }
    }

    private static bool PointInPolygon(RoutePointDto point, IReadOnlyList<RoutePointDto> polygon)
    {
        var inside = false;
        var j = polygon.Count - 1;
        for (var i = 0; i < polygon.Count; j = i++)
        {
            var pi = polygon[i];
            var pj = polygon[j];

            var intersects = ((pi.Y > point.Y) != (pj.Y > point.Y))
                && (point.X < (pj.X - pi.X) * (point.Y - pi.Y) / ((pj.Y - pi.Y) + double.Epsilon) + pi.X);

            if (intersects)
            {
                inside = !inside;
            }
        }

        return inside;
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

    private sealed class GridGraph(double[,] weights, bool[,] blocked)
    {
        public double[,] Weights { get; } = weights;
        public bool[,] Blocked { get; } = blocked;
    }
}
