using GO2.Api.Contracts;
using GO2.Api.Models;

namespace GO2.Api.Application.Routes;

// Routing engine on navmesh: graph nodes are object geometry vertices,
// edge weights are evaluated through navmesh traversal and rule priorities.
public sealed class RoutingEngineService
{
    private const double NavCellSize = 5;
    private const double NeighborRadius = 90;
    private const int MaxNeighborsPerNode = 10;

    public RouteGraphResponse BuildGraph(IReadOnlyCollection<TerrainObject> objects, RouteProfileDto profile)
    {
        var context = BuildRoutingContext(objects, profile);
        var nodes = context.Graph.Nodes.Values
            .Select(node => new RouteGraphNodeDto
            {
                Id = node.Id,
                X = node.Point.X,
                Y = node.Point.Y
            })
            .ToList();
        var edges = context.Graph.ToRouteEdges();

        return new RouteGraphResponse
        {
            Nodes = nodes,
            Edges = edges,
            GridWidth = context.NavMesh.Columns,
            GridHeight = context.NavMesh.Rows,
            Summary = nodes.Count == 0
                ? "Не удалось построить граф: отсутствуют проходимые вершины."
                : $"Построен navmesh-граф: {nodes.Count} узлов, {edges.Count} ребер."
        };
    }

    public RouteCalculationResultDto Calculate(
        IReadOnlyCollection<TerrainObject> objects,
        IReadOnlyList<RoutePointDto> waypoints,
        RouteProfileDto profile)
    {
        var context = BuildRoutingContext(objects, profile);
        var variants = BuildTop3(context.Graph, waypoints, profile);
        return new RouteCalculationResultDto
        {
            Routes = variants,
            Summary = variants.Count == 0
                ? "Маршруты не найдены для заданных точек."
                : $"Найдено {variants.Count} маршрут(а). Лучший вариант: №{variants[0].Rank}."
        };
    }

    public RouteCalculationResultDto CalculateFromGraph(
        RouteGraphResponse graphResponse,
        IReadOnlyList<RoutePointDto> waypoints,
        RouteProfileDto profile)
    {
        var graph = BuildObjectGraphFromResponse(graphResponse);
        var variants = BuildTop3(graph, waypoints, profile);
        return new RouteCalculationResultDto
        {
            Routes = variants,
            Summary = variants.Count == 0
                ? "Маршруты не найдены для заданных точек."
                : $"Найдено {variants.Count} маршрут(а). Лучший вариант: №{variants[0].Rank}."
        };
    }

    private static RoutingContext BuildRoutingContext(IReadOnlyCollection<TerrainObject> objects, RouteProfileDto profile)
    {
        var prepared = objects.Select(PrepareObject).Where(x => x is not null).Select(x => x!).ToList();
        var bounds = CalculateBounds(prepared);
        var areaFeatures = prepared.Where(x => x.GeometryKind == TerrainGeometryKind.Polygon).ToList();
        var lineFeatures = prepared.Where(x => x.GeometryKind == TerrainGeometryKind.Line).ToList();
        var navMesh = BuildNavMesh(areaFeatures, profile, bounds);
        var graph = BuildObjectGraph(prepared, areaFeatures, lineFeatures, navMesh, profile);
        return new RoutingContext(graph, navMesh);
    }

    private static ObjectGraph BuildObjectGraphFromResponse(RouteGraphResponse response)
    {
        var graph = new ObjectGraph();
        foreach (var node in response.Nodes)
        {
            graph.Nodes[node.Id] = new GraphNode(node.Id, new RoutePointDto { X = node.X, Y = node.Y });
        }

        foreach (var edge in response.Edges)
        {
            graph.AddUndirectedEdge(edge.FromNodeId, edge.ToNodeId, edge.Weight);
        }

        graph.RecalculateHeuristicScale();
        return graph;
    }

    private static NavMesh BuildNavMesh(
        IReadOnlyCollection<PreparedTerrainObject> areaFeatures,
        RouteProfileDto profile,
        MapBounds bounds)
    {
        var width = Math.Max(1.0, bounds.MaxX - bounds.MinX);
        var height = Math.Max(1.0, bounds.MaxY - bounds.MinY);
        var columns = Math.Max(1, (int)Math.Ceiling(width / NavCellSize));
        var rows = Math.Max(1, (int)Math.Ceiling(height / NavCellSize));
        var costs = new double[columns, rows];
        var blocked = new bool[columns, rows];
        var navMesh = new NavMesh(columns, rows, costs, blocked, bounds);

        for (var x = 0; x < columns; x++)
        {
            for (var y = 0; y < rows; y++)
            {
                var center = navMesh.ToCellCenter(x, y);
                var areaAtPoint = areaFeatures
                    .Where(a => PointInPolygon(center, a.Points))
                    .ToList();

                if (areaAtPoint.Any(a => a.IsImpassable))
                {
                    blocked[x, y] = true;
                    costs[x, y] = double.PositiveInfinity;
                    continue;
                }

                var areaCost = 1.0;
                foreach (var area in areaAtPoint)
                {
                    areaCost = Math.Max(areaCost, BuildSurfaceCost(area, profile));
                }

                blocked[x, y] = false;
                costs[x, y] = areaCost;
            }
        }

        return navMesh;
    }

    private static ObjectGraph BuildObjectGraph(
        IReadOnlyCollection<PreparedTerrainObject> objects,
        IReadOnlyCollection<PreparedTerrainObject> areaFeatures,
        IReadOnlyCollection<PreparedTerrainObject> lineFeatures,
        NavMesh navMesh,
        RouteProfileDto profile)
    {
        var graph = new ObjectGraph();

        foreach (var obj in objects)
        {
            for (var i = 0; i < obj.Points.Count; i++)
            {
                var nodeId = BuildObjectPointKey(obj.ObjectId, i);
                if (graph.Nodes.ContainsKey(nodeId))
                {
                    continue;
                }

                graph.Nodes[nodeId] = new GraphNode(nodeId, obj.Points[i]);
            }
        }

        // 1) Required edges along line objects. Line coefficient has priority over area.
        foreach (var line in lineFeatures)
        {
            for (var i = 1; i < line.Points.Count; i++)
            {
                var fromId = BuildObjectPointKey(line.ObjectId, i - 1);
                var toId = BuildObjectPointKey(line.ObjectId, i);
                if (!graph.Nodes.TryGetValue(fromId, out var fromNode) || !graph.Nodes.TryGetValue(toId, out var toNode))
                {
                    continue;
                }

                var cost = EstimateSegmentCost(
                    fromNode.Point,
                    toNode.Point,
                    areaFeatures,
                    profile,
                    lineTraversabilityOverride: line.Traversability);

                if (double.IsFinite(cost))
                {
                    graph.AddUndirectedEdge(fromId, toId, cost);
                }
            }
        }

        // 2) Additional local edges through navmesh cost.
        var nodeList = graph.Nodes.Values.ToList();
        var buckets = BuildSpatialBuckets(nodeList);
        var navPathCache = new Dictionary<string, double>();

        foreach (var node in nodeList)
        {
            var candidates = FindNeighborCandidates(node, nodeList, buckets)
                .Where(c => c.Id != node.Id && !graph.HasEdge(node.Id, c.Id))
                .Select(c => new { Node = c, Distance = Euclidean(node.Point, c.Point) })
                .Where(x => x.Distance <= NeighborRadius && x.Distance > 0.001)
                .OrderBy(x => x.Distance)
                .Take(MaxNeighborsPerNode)
                .ToList();

            foreach (var candidate in candidates)
            {
                var cost = EstimateNavMeshCost(node.Point, candidate.Node.Point, navMesh, navPathCache);
                if (!double.IsFinite(cost))
                {
                    continue;
                }

                graph.AddUndirectedEdge(node.Id, candidate.Node.Id, cost);
            }
        }

        graph.RecalculateHeuristicScale();
        return graph;
    }

    private List<RouteVariantDto> BuildTop3(ObjectGraph graph, IReadOnlyList<RoutePointDto> waypoints, RouteProfileDto profile)
    {
        if (waypoints.Count < 2 || graph.Nodes.Count == 0)
        {
            return [];
        }

        var snapped = waypoints
            .Select(point => SnapToGraphNode(graph, point))
            .ToList();

        if (snapped.Any(x => x is null))
        {
            return [];
        }

        var snappedWaypoints = snapped.Select(x => x!).ToList();
        var result = new List<RouteVariantDto>();
        var penalties = new Dictionary<string, double>();

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

            foreach (var nodeId in path)
            {
                penalties[nodeId] = penalties.GetValueOrDefault(nodeId) + 1.4 + i * 0.35;
            }
        }

        return result.OrderBy(x => x.TotalCost).Select((x, index) =>
        {
            x.Rank = index + 1;
            return x;
        }).ToList();
    }

    private static string? SnapToGraphNode(ObjectGraph graph, RoutePointDto point)
    {
        string? bestNodeId = null;
        var bestDistance = double.MaxValue;

        foreach (var node in graph.Nodes.Values)
        {
            var distance = Euclidean(point, node.Point);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestNodeId = node.Id;
            }
        }

        return bestNodeId;
    }

    private static List<string> BuildPathForWaypoints(
        ObjectGraph graph,
        Dictionary<string, double> penalties,
        IReadOnlyList<string> waypoints)
    {
        var all = new List<string>();
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

    private static List<string> AStar(
        ObjectGraph graph,
        Dictionary<string, double> penalties,
        string startNodeId,
        string endNodeId)
    {
        if (!graph.Nodes.ContainsKey(startNodeId) || !graph.Nodes.ContainsKey(endNodeId))
        {
            return [];
        }

        var open = new PriorityQueue<string, double>();
        var cameFrom = new Dictionary<string, string>();
        var g = new Dictionary<string, double> { [startNodeId] = 0 };
        open.Enqueue(startNodeId, 0);

        while (open.Count > 0)
        {
            var current = open.Dequeue();
            if (current == endNodeId)
            {
                return Reconstruct(cameFrom, current);
            }

            foreach (var edge in graph.GetNeighbors(current))
            {
                var next = edge.ToNodeId;
                var stepCost = edge.Weight + penalties.GetValueOrDefault(next);
                var tentative = g[current] + stepCost;

                if (!g.TryGetValue(next, out var known) || tentative < known)
                {
                    cameFrom[next] = current;
                    g[next] = tentative;
                    var f = tentative + Heuristic(graph, next, endNodeId);
                    open.Enqueue(next, f);
                }
            }
        }

        return [];
    }

    private static List<string> Reconstruct(Dictionary<string, string> cameFrom, string current)
    {
        var result = new List<string> { current };
        while (cameFrom.TryGetValue(current, out var previous))
        {
            result.Add(previous);
            current = previous;
        }

        result.Reverse();
        return result;
    }

    private static double Heuristic(ObjectGraph graph, string fromNodeId, string toNodeId)
    {
        var from = graph.Nodes[fromNodeId].Point;
        var to = graph.Nodes[toNodeId].Point;
        return Euclidean(from, to) * graph.MinWeightPerPixel;
    }

    private static RouteVariantDto BuildVariant(List<string> pathNodeIds, ObjectGraph graph, int rank, RouteProfileDto profile)
    {
        var points = pathNodeIds.Select(id => graph.Nodes[id].Point).ToList();
        var segments = new List<RouteSegmentDto>();
        double totalCost = 0;
        double risk = 0;
        double length = 0;

        for (var i = 1; i < pathNodeIds.Count; i++)
        {
            var fromId = pathNodeIds[i - 1];
            var toId = pathNodeIds[i];
            if (!graph.TryGetEdgeWeight(fromId, toId, out var edgeCost))
            {
                continue;
            }

            var fromPoint = graph.Nodes[fromId].Point;
            var toPoint = graph.Nodes[toId].Point;
            var distance = Euclidean(fromPoint, toPoint);
            var segmentRisk = edgeCost > 2.0 ? 0.9 : edgeCost > 1.4 ? 0.6 : 0.3;

            totalCost += edgeCost;
            risk += segmentRisk;
            length += distance;

            segments.Add(new RouteSegmentDto
            {
                From = fromPoint,
                To = toPoint,
                SegmentCost = Math.Round(edgeCost, 3),
                SegmentRisk = Math.Round(segmentRisk, 3)
            });
        }

        length = Math.Max(1.0, length);
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
                "Маршрут построен по графу точек объектов с учетом navmesh-проходимости."
            ]
        };
    }

    private static Dictionary<(int X, int Y), List<GraphNode>> BuildSpatialBuckets(IReadOnlyCollection<GraphNode> nodes)
    {
        var buckets = new Dictionary<(int X, int Y), List<GraphNode>>();
        foreach (var node in nodes)
        {
            var key = ToBucket(node.Point);
            if (!buckets.TryGetValue(key, out var list))
            {
                list = [];
                buckets[key] = list;
            }

            list.Add(node);
        }

        return buckets;
    }

    private static IEnumerable<GraphNode> FindNeighborCandidates(
        GraphNode node,
        IReadOnlyCollection<GraphNode> allNodes,
        IReadOnlyDictionary<(int X, int Y), List<GraphNode>> buckets)
    {
        var origin = ToBucket(node.Point);
        var found = new HashSet<string>();

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                var key = (origin.X + dx, origin.Y + dy);
                if (!buckets.TryGetValue(key, out var list))
                {
                    continue;
                }

                foreach (var candidate in list)
                {
                    if (found.Add(candidate.Id))
                    {
                        yield return candidate;
                    }
                }
            }
        }

        if (found.Count > 0)
        {
            yield break;
        }

        foreach (var candidate in allNodes)
        {
            yield return candidate;
        }
    }

    private static (int X, int Y) ToBucket(RoutePointDto point)
    {
        var x = (int)Math.Floor(point.X / NeighborRadius);
        var y = (int)Math.Floor(point.Y / NeighborRadius);
        return (x, y);
    }

    private static double EstimateNavMeshCost(
        RoutePointDto from,
        RoutePointDto to,
        NavMesh navMesh,
        Dictionary<string, double> cache)
    {
        var fromCell = navMesh.SnapToWalkableCell(from);
        var toCell = navMesh.SnapToWalkableCell(to);
        if (fromCell is null || toCell is null)
        {
            return double.PositiveInfinity;
        }

        var key = BuildPairKey(fromCell.Value.Id, toCell.Value.Id);
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var cost = FindMeshPathCost(navMesh, fromCell.Value, toCell.Value);
        cache[key] = cost;
        return cost;
    }

    private static double FindMeshPathCost(NavMesh navMesh, MeshCell fromCell, MeshCell toCell)
    {
        if (fromCell.Id == toCell.Id)
        {
            return Euclidean(fromCell.Center, toCell.Center) * navMesh.Costs[fromCell.X, fromCell.Y];
        }

        var open = new PriorityQueue<MeshCell, double>();
        var g = new Dictionary<int, double> { [fromCell.Id] = 0 };
        open.Enqueue(fromCell, 0);

        while (open.Count > 0)
        {
            var current = open.Dequeue();
            if (current.Id == toCell.Id)
            {
                return g[current.Id];
            }

            foreach (var next in navMesh.Neighbors(current))
            {
                var stepCost = Euclidean(current.Center, next.Center)
                    * ((navMesh.Costs[current.X, current.Y] + navMesh.Costs[next.X, next.Y]) / 2.0);
                var tentative = g[current.Id] + stepCost;

                if (!g.TryGetValue(next.Id, out var known) || tentative < known)
                {
                    g[next.Id] = tentative;
                    var f = tentative + Euclidean(next.Center, toCell.Center) * navMesh.MinCost;
                    open.Enqueue(next, f);
                }
            }
        }

        return double.PositiveInfinity;
    }

    private static double EstimateSegmentCost(
        RoutePointDto from,
        RoutePointDto to,
        IReadOnlyCollection<PreparedTerrainObject> areaFeatures,
        RouteProfileDto profile,
        decimal? lineTraversabilityOverride = null)
    {
        if (SegmentBlockedByImpassableArea(from, to, areaFeatures))
        {
            return double.PositiveInfinity;
        }

        var distance = Euclidean(from, to);
        if (distance <= 0.001)
        {
            return 0;
        }

        var areaBase = EstimateAreaBaseCostOnSegment(from, to, areaFeatures, profile);
        var appliedCost = areaBase;

        // Priority: impassable > line override > area base > default.
        if (lineTraversabilityOverride.HasValue)
        {
            var lineProxy = new PreparedTerrainObject(
                Guid.Empty,
                TerrainGeometryKind.Line,
                TerrainClass.ManMade,
                lineTraversabilityOverride.Value,
                false,
                []);
            appliedCost = BuildSurfaceCost(lineProxy, profile);
        }

        return distance * appliedCost;
    }

    private static bool SegmentBlockedByImpassableArea(
        RoutePointDto from,
        RoutePointDto to,
        IReadOnlyCollection<PreparedTerrainObject> areaFeatures)
    {
        var blockers = areaFeatures.Where(a => a.IsImpassable).ToList();
        if (blockers.Count == 0)
        {
            return false;
        }

        var samples = Math.Max(3, (int)Math.Ceiling(Euclidean(from, to) / 8.0));
        for (var i = 0; i <= samples; i++)
        {
            var t = i / (double)samples;
            var sample = new RoutePointDto
            {
                X = from.X + (to.X - from.X) * t,
                Y = from.Y + (to.Y - from.Y) * t
            };

            if (blockers.Any(blocker => PointInPolygon(sample, blocker.Points)))
            {
                return true;
            }
        }

        return false;
    }

    private static double EstimateAreaBaseCostOnSegment(
        RoutePointDto from,
        RoutePointDto to,
        IReadOnlyCollection<PreparedTerrainObject> areaFeatures,
        RouteProfileDto profile)
    {
        var samples = Math.Max(3, (int)Math.Ceiling(Euclidean(from, to) / 12.0));
        var aggregated = 0.0;

        for (var i = 0; i <= samples; i++)
        {
            var t = i / (double)samples;
            var sample = new RoutePointDto
            {
                X = from.X + (to.X - from.X) * t,
                Y = from.Y + (to.Y - from.Y) * t
            };

            var sampleCost = 1.0;
            foreach (var area in areaFeatures.Where(a => PointInPolygon(sample, a.Points)))
            {
                sampleCost = Math.Max(sampleCost, BuildSurfaceCost(area, profile));
            }

            aggregated += sampleCost;
        }

        return aggregated / (samples + 1);
    }

    private static double BuildSurfaceCost(PreparedTerrainObject obj, RouteProfileDto profile)
    {
        var traversability = (double)obj.Traversability;
        var terrainRisk = obj.TerrainClass switch
        {
            TerrainClass.Water => 2.2,
            TerrainClass.Rock => 1.7,
            TerrainClass.Vegetation => 1.25,
            TerrainClass.ManMade => 1.1,
            _ => 1.0
        };
        var movementPenalty = Math.Clamp(1.0 / Math.Max(0.05, traversability), 0.2, 10.0);
        return profile.TimeWeight * movementPenalty + profile.SafetyWeight * terrainRisk;
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
            traversability <= 0.05m,
            points);
    }

    private static decimal GetEffectiveTraversability(TerrainObject obj)
    {
        if (obj.TerrainObjectType is not null)
        {
            return obj.TerrainObjectType.Traversability;
        }

        return obj.Traversability;
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
        var allPoints = objects.SelectMany(x => x.Points).ToList();
        if (allPoints.Count == 0)
        {
            return new MapBounds(0, 0, 1, 1);
        }

        var minX = allPoints.Min(p => p.X);
        var minY = allPoints.Min(p => p.Y);
        var maxX = allPoints.Max(p => p.X);
        var maxY = allPoints.Max(p => p.Y);
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

    private static double Euclidean(RoutePointDto a, RoutePointDto b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static string BuildObjectPointKey(Guid objectId, int pointIndex)
    {
        return $"{objectId:N}:{pointIndex}";
    }

    private static string BuildPairKey(int a, int b)
    {
        return a <= b ? $"{a}:{b}" : $"{b}:{a}";
    }

    private sealed record RoutingContext(ObjectGraph Graph, NavMesh NavMesh);
    private sealed record MapBounds(double MinX, double MinY, double MaxX, double MaxY);

    private sealed record PreparedTerrainObject(
        Guid ObjectId,
        TerrainGeometryKind GeometryKind,
        TerrainClass TerrainClass,
        decimal Traversability,
        bool IsImpassable,
        IReadOnlyList<RoutePointDto> Points);

    private sealed record GraphNode(string Id, RoutePointDto Point);

    private sealed class ObjectGraph
    {
        public Dictionary<string, GraphNode> Nodes { get; } = [];
        private readonly Dictionary<string, List<RouteGraphEdgeDto>> _adjacency = [];
        private readonly Dictionary<string, double> _edgeWeights = [];
        public double MinWeightPerPixel { get; private set; } = 0.8;

        public void AddUndirectedEdge(string fromId, string toId, double weight)
        {
            if (fromId == toId || !double.IsFinite(weight) || weight <= 0)
            {
                return;
            }

            var key = BuildPairKeyForNodes(fromId, toId);
            if (_edgeWeights.TryGetValue(key, out var existing) && existing <= weight)
            {
                return;
            }

            _edgeWeights[key] = weight;
            AddDirected(fromId, toId, weight);
            AddDirected(toId, fromId, weight);
        }

        public bool HasEdge(string fromId, string toId)
        {
            return _edgeWeights.ContainsKey(BuildPairKeyForNodes(fromId, toId));
        }

        public IEnumerable<RouteGraphEdgeDto> GetNeighbors(string nodeId)
        {
            if (_adjacency.TryGetValue(nodeId, out var edges))
            {
                return edges;
            }

            return [];
        }

        public bool TryGetEdgeWeight(string fromId, string toId, out double weight)
        {
            return _edgeWeights.TryGetValue(BuildPairKeyForNodes(fromId, toId), out weight);
        }

        public List<RouteGraphEdgeDto> ToRouteEdges()
        {
            return _edgeWeights
                .Select(x =>
                {
                    var split = x.Key.Split('|');
                    return new RouteGraphEdgeDto
                    {
                        FromNodeId = split[0],
                        ToNodeId = split[1],
                        Weight = Math.Round(x.Value, 3)
                    };
                })
                .ToList();
        }

        public void RecalculateHeuristicScale()
        {
            var ratios = _edgeWeights
                .Select(x =>
                {
                    var parts = x.Key.Split('|');
                    if (!Nodes.TryGetValue(parts[0], out var from) || !Nodes.TryGetValue(parts[1], out var to))
                    {
                        return double.PositiveInfinity;
                    }

                    var distance = Euclidean(from.Point, to.Point);
                    return distance <= 0.001 ? double.PositiveInfinity : x.Value / distance;
                })
                .Where(double.IsFinite)
                .ToList();

            MinWeightPerPixel = ratios.Count == 0 ? 0.8 : Math.Max(0.01, ratios.Min());
        }

        private void AddDirected(string fromId, string toId, double weight)
        {
            if (!_adjacency.TryGetValue(fromId, out var list))
            {
                list = [];
                _adjacency[fromId] = list;
            }

            list.RemoveAll(x => x.ToNodeId == toId);
            list.Add(new RouteGraphEdgeDto
            {
                FromNodeId = fromId,
                ToNodeId = toId,
                Weight = weight
            });
        }

        private static string BuildPairKeyForNodes(string a, string b)
        {
            return string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";
        }
    }

    private readonly record struct MeshCell(int Id, int X, int Y, RoutePointDto Center);

    private sealed class NavMesh(int columns, int rows, double[,] costs, bool[,] blocked, MapBounds bounds)
    {
        public int Columns { get; } = columns;
        public int Rows { get; } = rows;
        public double[,] Costs { get; } = costs;
        public bool[,] Blocked { get; } = blocked;
        public MapBounds Bounds { get; } = bounds;
        public double MinCost { get; } = GetMinCost(costs, blocked);

        public MeshCell? SnapToWalkableCell(RoutePointDto point)
        {
            var startX = Math.Clamp((int)Math.Floor((point.X - Bounds.MinX) / NavCellSize), 0, Columns - 1);
            var startY = Math.Clamp((int)Math.Floor((point.Y - Bounds.MinY) / NavCellSize), 0, Rows - 1);

            if (!Blocked[startX, startY])
            {
                return CreateCell(startX, startY);
            }

            for (var radius = 1; radius <= Math.Max(Columns, Rows); radius++)
            {
                MeshCell? best = null;
                var bestDistance = double.MaxValue;

                for (var dx = -radius; dx <= radius; dx++)
                {
                    for (var dy = -radius; dy <= radius; dy++)
                    {
                        if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                        {
                            continue;
                        }

                        var x = startX + dx;
                        var y = startY + dy;
                        if (x < 0 || y < 0 || x >= Columns || y >= Rows || Blocked[x, y])
                        {
                            continue;
                        }

                        var center = ToCellCenter(x, y);
                        var distance = Euclidean(point, center);
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            best = CreateCell(x, y);
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

        public IEnumerable<MeshCell> Neighbors(MeshCell cell)
        {
            var neighbors = new[]
            {
                (X: cell.X + 1, Y: cell.Y),
                (X: cell.X - 1, Y: cell.Y),
                (X: cell.X, Y: cell.Y + 1),
                (X: cell.X, Y: cell.Y - 1)
            };

            foreach (var next in neighbors)
            {
                if (next.X < 0 || next.Y < 0 || next.X >= Columns || next.Y >= Rows || Blocked[next.X, next.Y])
                {
                    continue;
                }

                yield return CreateCell(next.X, next.Y);
            }
        }

        private MeshCell CreateCell(int x, int y)
        {
            var id = y * Columns + x;
            return new MeshCell(id, x, y, ToCellCenter(x, y));
        }

        public RoutePointDto ToCellCenter(int x, int y)
        {
            var cx = Math.Min(Bounds.MaxX, Bounds.MinX + x * NavCellSize + NavCellSize / 2.0);
            var cy = Math.Min(Bounds.MaxY, Bounds.MinY + y * NavCellSize + NavCellSize / 2.0);
            return new RoutePointDto { X = cx, Y = cy };
        }

        private static double GetMinCost(double[,] costs, bool[,] blocked)
        {
            var cols = costs.GetLength(0);
            var rows = costs.GetLength(1);
            var min = double.PositiveInfinity;

            for (var x = 0; x < cols; x++)
            {
                for (var y = 0; y < rows; y++)
                {
                    if (blocked[x, y])
                    {
                        continue;
                    }

                    min = Math.Min(min, costs[x, y]);
                }
            }

            return double.IsFinite(min) ? Math.Max(0.01, min) : 1.0;
        }
    }
}

