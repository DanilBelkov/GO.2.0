using GO2.Api.Contracts;
using GO2.Api.Models;

namespace GO2.Api.Application.Routes;

// Маршрутизация по navmesh:
// 1) Из объектов карты строится сетка проходимости.
// 2) Из сетки строится граф (узел = проходимая ячейка, ребра = соседние ячейки).
// 3) Точки пользователя привязываются к ближайшим узлам графа.
public sealed class RoutingEngineService
{
    // Целевая плотность navmesh, чтобы не взрывать время/память на очень больших картах.
    private const int TargetNavCells = 18_000;
    private const int MaxNavCells = 45_000;
    private const double MinCellSize = 3.0;
    private const double MaxCellSize = 120.0;

    public RouteGraphResponse BuildGraph(IReadOnlyCollection<TerrainObject> objects, RouteProfileDto profile)
    {
        var prepared = objects.Select(PrepareObject).Where(x => x is not null).Select(x => x!).ToList();
        if (prepared.Count == 0)
        {
            return new RouteGraphResponse
            {
                Nodes = [],
                Edges = [],
                GridWidth = 0,
                GridHeight = 0,
                Summary = "Не удалось построить граф: нет объектов оцифровки."
            };
        }

        var bounds = CalculateBounds(prepared);
        var cellSize = ResolveCellSize(bounds);
        var cols = Math.Max(1, (int)Math.Ceiling((bounds.MaxX - bounds.MinX) / cellSize) + 1);
        var rows = Math.Max(1, (int)Math.Ceiling((bounds.MaxY - bounds.MinY) / cellSize) + 1);

        var costs = new double[cols, rows];
        var blocked = new bool[cols, rows];
        for (var x = 0; x < cols; x++)
        {
            for (var y = 0; y < rows; y++)
            {
                costs[x, y] = 1.0;
                blocked[x, y] = false;
            }
        }

        var areas = prepared.Where(x => x.GeometryKind == TerrainGeometryKind.Polygon).ToList();
        var lines = prepared.Where(x => x.GeometryKind == TerrainGeometryKind.Line).ToList();

        RasterizeAreas(areas, costs, blocked, bounds, cellSize, profile);
        RasterizeLines(lines, costs, blocked, bounds, cellSize, profile);

        return BuildGraphFromMesh(costs, blocked, bounds, cellSize);
    }

    // Расчет маршрутов напрямую по уже готовому графу (из БД),
    // чтобы не пересобирать navmesh на каждом запросе.
    public RouteCalculationResultDto CalculateFromGraph(
        RouteGraphResponse graph,
        IReadOnlyList<RoutePointDto> waypoints,
        RouteProfileDto profile)
    {
        var variants = BuildTop3(graph, waypoints, profile);
        return new RouteCalculationResultDto
        {
            Routes = variants,
            Summary = variants.Count == 0
                ? "Маршруты не найдены для заданных точек."
                : $"Найдено {variants.Count} маршрут(а). Лучший вариант: №{variants[0].Rank}."
        };
    }

    // Режим совместимости: если графа в БД нет, строим его из объектов,
    // затем считаем маршруты по нему.
    public RouteCalculationResultDto Calculate(
        IReadOnlyCollection<TerrainObject> objects,
        IReadOnlyList<RoutePointDto> waypoints,
        RouteProfileDto profile)
    {
        var graph = BuildGraph(objects, profile);
        return CalculateFromGraph(graph, waypoints, profile);
    }

    private static RouteGraphResponse BuildGraphFromMesh(
        double[,] costs,
        bool[,] blocked,
        MapBounds bounds,
        double cellSize)
    {
        var cols = costs.GetLength(0);
        var rows = costs.GetLength(1);

        var nodes = new List<RouteGraphNodeDto>(cols * rows / 2);
        var edges = new List<RouteGraphEdgeDto>(cols * rows);

        // Быстрые индексы: сетка -> id узла.
        var nodeIds = new string?[cols, rows];

        for (var x = 0; x < cols; x++)
        {
            for (var y = 0; y < rows; y++)
            {
                if (blocked[x, y])
                {
                    continue;
                }

                var id = BuildCellNodeId(x, y);
                nodeIds[x, y] = id;
                var center = CellCenter(bounds, cellSize, x, y);
                nodes.Add(new RouteGraphNodeDto
                {
                    Id = id,
                    X = center.X,
                    Y = center.Y
                });
            }
        }

        // Создаем ребра без дублей: вправо, вниз, и диагонали вниз.
        for (var x = 0; x < cols; x++)
        {
            for (var y = 0; y < rows; y++)
            {
                var fromId = nodeIds[x, y];
                if (fromId is null)
                {
                    continue;
                }

                AddEdgeIfWalkable(nodeIds, costs, edges, x, y, x + 1, y, 1.0);
                AddEdgeIfWalkable(nodeIds, costs, edges, x, y, x, y + 1, 1.0);
                AddEdgeIfWalkable(nodeIds, costs, edges, x, y, x + 1, y + 1, Math.Sqrt(2));
                AddEdgeIfWalkable(nodeIds, costs, edges, x, y, x - 1, y + 1, Math.Sqrt(2));
            }
        }

        return new RouteGraphResponse
        {
            Nodes = nodes,
            Edges = edges,
            GridWidth = cols,
            GridHeight = rows,
            Summary = nodes.Count == 0
                ? "Не удалось построить граф: вся карта непроходима."
                : $"Navmesh: {nodes.Count} узлов, {edges.Count} ребер, размер ячейки {Math.Round(cellSize, 2)}."
        };
    }

    private static void AddEdgeIfWalkable(
        string?[,] nodeIds,
        double[,] costs,
        List<RouteGraphEdgeDto> edges,
        int fx,
        int fy,
        int tx,
        int ty,
        double metric)
    {
        if (tx < 0 || ty < 0 || tx >= nodeIds.GetLength(0) || ty >= nodeIds.GetLength(1))
        {
            return;
        }

        var fromId = nodeIds[fx, fy];
        var toId = nodeIds[tx, ty];
        if (fromId is null || toId is null)
        {
            return;
        }

        var weight = metric * ((costs[fx, fy] + costs[tx, ty]) / 2.0);
        edges.Add(new RouteGraphEdgeDto
        {
            FromNodeId = fromId,
            ToNodeId = toId,
            Weight = Math.Round(weight, 4)
        });
    }

    private List<RouteVariantDto> BuildTop3(RouteGraphResponse graph, IReadOnlyList<RoutePointDto> waypoints, RouteProfileDto profile)
    {
        if (waypoints.Count < 2 || graph.Nodes.Count == 0)
        {
            return [];
        }

        var nodeById = graph.Nodes.ToDictionary(n => n.Id, n => n);
        var adjacency = BuildAdjacency(graph);

        var snapped = waypoints
            .Select(point => SnapToNearestNode(graph.Nodes, point)?.Id)
            .ToList();

        if (snapped.Any(x => x is null))
        {
            return [];
        }

        var snappedIds = snapped.Select(x => x!).ToList();
        var penalties = new Dictionary<string, double>();
        var result = new List<RouteVariantDto>();

        for (var i = 0; i < 6 && result.Count < 3; i++)
        {
            var path = BuildPathForWaypoints(adjacency, nodeById, penalties, snappedIds);
            if (path.Count == 0)
            {
                break;
            }

            var candidate = BuildVariant(path, adjacency, nodeById, result.Count + 1, profile);
            var hasHighOverlap = result.Any(existing => Overlap(existing.Polyline, candidate.Polyline) > 0.7);
            if (!hasHighOverlap)
            {
                result.Add(candidate);
            }

            // Штрафуем использованные узлы, чтобы получить разнообразные альтернативы.
            foreach (var nodeId in path)
            {
                penalties[nodeId] = penalties.GetValueOrDefault(nodeId) + 1.3 + i * 0.35;
            }
        }

        return result.OrderBy(x => x.TotalCost).Select((x, idx) =>
        {
            x.Rank = idx + 1;
            return x;
        }).ToList();
    }

    private static List<string> BuildPathForWaypoints(
        Dictionary<string, List<GraphEdge>> adjacency,
        Dictionary<string, RouteGraphNodeDto> nodeById,
        Dictionary<string, double> penalties,
        IReadOnlyList<string> waypoints)
    {
        var all = new List<string>();
        for (var i = 0; i < waypoints.Count - 1; i++)
        {
            var segment = AStar(adjacency, nodeById, penalties, waypoints[i], waypoints[i + 1]);
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

    private static List<string> AStar(
        Dictionary<string, List<GraphEdge>> adjacency,
        Dictionary<string, RouteGraphNodeDto> nodeById,
        Dictionary<string, double> penalties,
        string startId,
        string endId)
    {
        if (!nodeById.ContainsKey(startId) || !nodeById.ContainsKey(endId))
        {
            return [];
        }

        var minEdgeWeight = adjacency.Values.SelectMany(x => x).Select(x => x.Weight).DefaultIfEmpty(1.0).Min();
        var open = new PriorityQueue<string, double>();
        var cameFrom = new Dictionary<string, string>();
        var g = new Dictionary<string, double> { [startId] = 0 };
        open.Enqueue(startId, 0);

        while (open.Count > 0)
        {
            var current = open.Dequeue();
            if (current == endId)
            {
                return Reconstruct(cameFrom, current);
            }

            if (!adjacency.TryGetValue(current, out var edges))
            {
                continue;
            }

            foreach (var edge in edges)
            {
                var next = edge.To;
                var tentative = g[current] + edge.Weight + penalties.GetValueOrDefault(next);
                if (!g.TryGetValue(next, out var known) || tentative < known)
                {
                    cameFrom[next] = current;
                    g[next] = tentative;
                    var heuristic = Euclidean(nodeById[next], nodeById[endId]) * Math.Max(0.01, minEdgeWeight);
                    open.Enqueue(next, tentative + heuristic);
                }
            }
        }

        return [];
    }

    private static List<string> Reconstruct(Dictionary<string, string> cameFrom, string current)
    {
        var result = new List<string> { current };
        while (cameFrom.TryGetValue(current, out var prev))
        {
            result.Add(prev);
            current = prev;
        }

        result.Reverse();
        return result;
    }

    private static RouteVariantDto BuildVariant(
        List<string> nodePath,
        Dictionary<string, List<GraphEdge>> adjacency,
        Dictionary<string, RouteGraphNodeDto> nodeById,
        int rank,
        RouteProfileDto profile)
    {
        var polyline = nodePath
            .Select(id => new RoutePointDto { X = nodeById[id].X, Y = nodeById[id].Y })
            .ToList();

        var segments = new List<RouteSegmentDto>();
        double totalCost = 0;
        double riskAcc = 0;
        double length = 0;

        for (var i = 1; i < nodePath.Count; i++)
        {
            var from = nodePath[i - 1];
            var to = nodePath[i];
            var edge = adjacency[from].FirstOrDefault(e => e.To == to);
            if (edge is null)
            {
                continue;
            }

            var fromNode = nodeById[from];
            var toNode = nodeById[to];
            var dist = Euclidean(fromNode, toNode);
            var segmentCost = edge.Weight;
            var segmentRisk = segmentCost > 2.0 ? 0.9 : segmentCost > 1.4 ? 0.6 : 0.3;

            totalCost += segmentCost;
            riskAcc += segmentRisk;
            length += dist;

            segments.Add(new RouteSegmentDto
            {
                From = new RoutePointDto { X = fromNode.X, Y = fromNode.Y },
                To = new RoutePointDto { X = toNode.X, Y = toNode.Y },
                SegmentCost = Math.Round(segmentCost, 4),
                SegmentRisk = Math.Round(segmentRisk, 3)
            });
        }

        length = Math.Max(1.0, length);
        var estimatedTime = length * (0.7 + profile.TimeWeight);
        var penalty = totalCost * profile.SafetyWeight;

        return new RouteVariantDto
        {
            Rank = rank,
            TotalCost = Math.Round(totalCost, 4),
            Length = Math.Round(length, 2),
            EstimatedTime = Math.Round(estimatedTime, 2),
            RiskScore = Math.Round(riskAcc / Math.Max(1, segments.Count), 3),
            PenaltyScore = Math.Round(penalty, 3),
            Polyline = polyline,
            Segments = segments,
            WhyChosen =
            [
                "Маршрут рассчитан напрямую по navmesh-графу.",
                "Точки пользователя привязаны к ближайшим проходимым узлам mesh.",
                $"Баланс времени/безопасности: {profile.TimeWeight:0.##}/{profile.SafetyWeight:0.##}."
            ]
        };
    }

    private static Dictionary<string, List<GraphEdge>> BuildAdjacency(RouteGraphResponse graph)
    {
        var adjacency = graph.Nodes.ToDictionary(n => n.Id, _ => new List<GraphEdge>());

        // Считаем граф неориентированным: добавляем обратные ребра.
        foreach (var e in graph.Edges)
        {
            if (!adjacency.ContainsKey(e.FromNodeId) || !adjacency.ContainsKey(e.ToNodeId))
            {
                continue;
            }

            adjacency[e.FromNodeId].Add(new GraphEdge(e.ToNodeId, e.Weight));
            adjacency[e.ToNodeId].Add(new GraphEdge(e.FromNodeId, e.Weight));
        }

        return adjacency;
    }

    private static RouteGraphNodeDto? SnapToNearestNode(IReadOnlyList<RouteGraphNodeDto> nodes, RoutePointDto point)
    {
        if (nodes.Count == 0)
        {
            return null;
        }

        RouteGraphNodeDto? best = null;
        var bestDistance = double.PositiveInfinity;
        foreach (var node in nodes)
        {
            var dx = node.X - point.X;
            var dy = node.Y - point.Y;
            var d = dx * dx + dy * dy;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = node;
            }
        }

        return best;
    }

    private static void RasterizeAreas(
        IReadOnlyCollection<PreparedTerrainObject> areas,
        double[,] costs,
        bool[,] blocked,
        MapBounds bounds,
        double cellSize,
        RouteProfileDto profile)
    {
        foreach (var area in areas)
        {
            if (area.Points.Count < 3)
            {
                continue;
            }

            var (minX, minY, maxX, maxY) = GetPointBounds(area.Points);
            var minCellX = Math.Max(0, (int)Math.Floor((minX - bounds.MinX) / cellSize));
            var maxCellX = Math.Min(costs.GetLength(0) - 1, (int)Math.Ceiling((maxX - bounds.MinX) / cellSize));
            var minCellY = Math.Max(0, (int)Math.Floor((minY - bounds.MinY) / cellSize));
            var maxCellY = Math.Min(costs.GetLength(1) - 1, (int)Math.Ceiling((maxY - bounds.MinY) / cellSize));

            var isImpassable = area.Traversability <= 0m;
            var areaCost = BuildSurfaceCost(area, profile);

            for (var x = minCellX; x <= maxCellX; x++)
            {
                for (var y = minCellY; y <= maxCellY; y++)
                {
                    var center = CellCenter(bounds, cellSize, x, y);
                    if (!PointInPolygon(center, area.Points))
                    {
                        continue;
                    }

                    if (isImpassable)
                    {
                        blocked[x, y] = true;
                        costs[x, y] = double.PositiveInfinity;
                        continue;
                    }

                    if (!blocked[x, y])
                    {
                        costs[x, y] = Math.Max(costs[x, y], areaCost);
                    }
                }
            }
        }
    }

    private static void RasterizeLines(
        IReadOnlyCollection<PreparedTerrainObject> lines,
        double[,] costs,
        bool[,] blocked,
        MapBounds bounds,
        double cellSize,
        RouteProfileDto profile)
    {
        foreach (var line in lines)
        {
            if (line.Points.Count < 2 || line.Traversability <= 0m)
            {
                continue;
            }

            var lineCost = BuildSurfaceCost(line, profile);

            for (var i = 1; i < line.Points.Count; i++)
            {
                var from = line.Points[i - 1];
                var to = line.Points[i];
                var dist = Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y));
                var steps = Math.Max(1, (int)Math.Ceiling(dist / Math.Max(1.0, cellSize * 0.6)));

                for (var s = 0; s <= steps; s++)
                {
                    var t = s / (double)steps;
                    var p = new RoutePointDto
                    {
                        X = from.X + (to.X - from.X) * t,
                        Y = from.Y + (to.Y - from.Y) * t
                    };

                    var (cx, cy) = ToCell(bounds, cellSize, p);
                    if (cx < 0 || cy < 0 || cx >= costs.GetLength(0) || cy >= costs.GetLength(1))
                    {
                        continue;
                    }

                    if (blocked[cx, cy])
                    {
                        continue;
                    }

                    // Приоритет линии: при движении по линии даем более выгодную стоимость.
                    costs[cx, cy] = Math.Min(costs[cx, cy], lineCost);
                }
            }
        }
    }

    private static double ResolveCellSize(MapBounds bounds)
    {
        var width = Math.Max(1.0, bounds.MaxX - bounds.MinX);
        var height = Math.Max(1.0, bounds.MaxY - bounds.MinY);
        var area = width * height;

        var cell = Math.Sqrt(area / TargetNavCells);
        cell = Math.Clamp(cell, MinCellSize, MaxCellSize);

        // Страховка для очень больших карт/выбросов координат.
        var cols = Math.Max(1, (int)Math.Ceiling(width / cell) + 1);
        var rows = Math.Max(1, (int)Math.Ceiling(height / cell) + 1);
        var total = cols * rows;
        if (total > MaxNavCells)
        {
            var k = Math.Sqrt(total / (double)MaxNavCells);
            cell *= k;
        }

        return Math.Clamp(cell, MinCellSize, MaxCellSize);
    }

    private static RoutePointDto CellCenter(MapBounds bounds, double cellSize, int x, int y)
    {
        return new RoutePointDto
        {
            X = bounds.MinX + x * cellSize,
            Y = bounds.MinY + y * cellSize
        };
    }

    private static (int X, int Y) ToCell(MapBounds bounds, double cellSize, RoutePointDto p)
    {
        var x = (int)Math.Round((p.X - bounds.MinX) / cellSize);
        var y = (int)Math.Round((p.Y - bounds.MinY) / cellSize);
        return (x, y);
    }

    private static string BuildCellNodeId(int x, int y)
    {
        return $"c:{x}:{y}";
    }

    private static double BuildSurfaceCost(PreparedTerrainObject obj, RouteProfileDto profile)
    {
        var traversability = (double)obj.Traversability;
        var terrainRisk = obj.TerrainClass switch
        {
            TerrainClass.Hydrography => 2.2,
            TerrainClass.RocksAndStones => 1.7,
            TerrainClass.Vegetation => 1.25,
            TerrainClass.ManMade => 1.1,
            TerrainClass.SkiTrackMarkings => 0.9,
            TerrainClass.CourseMarkings => 1.0,
            TerrainClass.TechnicalSymbols => 1.0,
            _ => 1.0
        };

        var movementPenalty = Math.Clamp(100.0 / Math.Max(1.0, traversability), 0.2, 100.0);
        return profile.TimeWeight * movementPenalty + profile.SafetyWeight * terrainRisk;
    }

    private static PreparedTerrainObject? PrepareObject(TerrainObject obj)
    {
        var points = ParsePoints(obj);
        if (points.Count == 0)
        {
            return null;
        }

        var traversability = GetEffectiveTraversability(obj);
        return new PreparedTerrainObject(
            obj.Id,
            obj.GeometryKind,
            obj.TerrainClass,
            traversability,
            points);
    }

    private static decimal GetEffectiveTraversability(TerrainObject obj)
    {
        return obj.TerrainObjectType?.Traversability ?? obj.Traversability;
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

    private static MapBounds CalculateBounds(IReadOnlyCollection<PreparedTerrainObject> objects)
    {
        var points = objects.SelectMany(x => x.Points).ToList();
        if (points.Count == 0)
        {
            return new MapBounds(0, 0, 1, 1);
        }

        var minX = points.Min(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxX = points.Max(p => p.X);
        var maxY = points.Max(p => p.Y);

        if (Math.Abs(maxX - minX) < 0.001)
        {
            maxX = minX + 1;
        }

        if (Math.Abs(maxY - minY) < 0.001)
        {
            maxY = minY + 1;
        }

        return new MapBounds(minX, minY, maxX, maxY);
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) GetPointBounds(IReadOnlyList<RoutePointDto> points)
    {
        var minX = points.Min(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxX = points.Max(p => p.X);
        var maxY = points.Max(p => p.Y);
        return (minX, minY, maxX, maxY);
    }

    private static bool PointInPolygon(RoutePointDto point, IReadOnlyList<RoutePointDto> polygon)
    {
        if (polygon.Count < 3)
        {
            return false;
        }

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

    private static double Euclidean(RouteGraphNodeDto a, RouteGraphNodeDto b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private sealed record PreparedTerrainObject(
        Guid Id,
        TerrainGeometryKind GeometryKind,
        TerrainClass TerrainClass,
        decimal Traversability,
        IReadOnlyList<RoutePointDto> Points);

    private sealed record MapBounds(double MinX, double MinY, double MaxX, double MaxY);

    private sealed record GraphEdge(string To, double Weight);
}
