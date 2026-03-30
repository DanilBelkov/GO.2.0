import { useEffect, useMemo, useState } from 'react';
import type { ChangeEvent } from 'react';
import { Circle, Image as KonvaImage, Layer, Line, Stage } from 'react-konva';
import type Konva from 'konva';
import { useNavigate } from 'react-router-dom';
import { clearAuth } from '../lib/auth';
import {
  calculateRoutes,
  createTerrainType,
  deleteTerrainType,
  getDigitizationStatus,
  getMap,
  getMapImageObjectUrl,
  getMapVersions,
  getMaps,
  getRouteGraph,
  getRouteJobStatus,
  getTerrainObjects,
  getTerrainTypes,
  saveTerrainObjects,
  startDigitization,
  updateTerrainType,
  uploadMap,
  uploadOcdMap,
} from '../lib/api';
import { getTerrainTypeImageUrl } from '../lib/terrainSymbolImages';
import type {
  DigitizationJob,
  MapDetails,
  MapListItem,
  MapVersion,
  RoutePoint,
  RouteGraph,
  RouteVariant,
  TerrainClass,
  TerrainGeometryKind,
  TerrainObject,
  TerrainType,
} from '../lib/api';

type ToolMode = 'select' | 'point' | 'line' | 'polygon' | 'route-point';

type EditorObject = {
  id: string;
  geometryKind: TerrainGeometryKind;
  terrainClass: TerrainClass;
  terrainObjectTypeId: string | null;
  traversability: number;
  points: RoutePoint[];
  source: 'Auto' | 'Manual';
};

type GraphEdgeLine = {
  from: RoutePoint;
  to: RoutePoint;
  weight: number;
};

type RouteRun = {
  id: string;
  createdAt: string;
  points: RoutePoint[];
  routes: RouteVariant[];
};

type MapsTab = 'ingest' | 'editor' | 'routes';

const CANVAS_WIDTH = 1320;
const CANVAS_HEIGHT = 760;
const POINT_RADIUS = 1;
const POINT_RADIUS_SELECTED = 2;
const OBJECT_STROKE_WIDTH = 0.4;
const OBJECT_STROKE_WIDTH_SELECTED = 0.8;
const VERTEX_RADIUS = 1.5;

const CLASS_OPTIONS: TerrainClass[] = [
  'Vegetation',
  'Hydrography',
  'Relief',
  'ManMade',
  'RocksAndStones',
  'CourseMarkings',
  'SkiTrackMarkings',
  'TechnicalSymbols',
];
const CLASS_LABELS_RU: Record<TerrainClass, string> = {
  Vegetation: 'Растительность',
  Hydrography: 'Гидрография',
  Relief: 'Рельеф',
  ManMade: 'Искусственные объекты',
  RocksAndStones: 'Скалы и камни',
  CourseMarkings: 'Обозначения дистанции',
  SkiTrackMarkings: 'Обозначения лыжней',
  TechnicalSymbols: 'Технические символы',
};
const OBJECT_COLORS: Record<TerrainClass, string> = {
  Vegetation: '#34D399',
  Hydrography: '#60A5FA',
  Relief: '#FBBF24',
  ManMade: '#FB7185',
  RocksAndStones: '#9CA3AF',
  CourseMarkings: '#A855F7',
  SkiTrackMarkings: '#16A34A',
  TechnicalSymbols: '#0EA5E9',
};
// Полноценная рабочая страница: карты, редактор объектов, маршрутизация, история и экспорт UI.
export function MapsPage() {
  const navigate = useNavigate();

  const [maps, setMaps] = useState<MapListItem[]>([]);
  const [selectedMapId, setSelectedMapId] = useState<string | null>(null);
  const [mapDetails, setMapDetails] = useState<MapDetails | null>(null);
  const [versions, setVersions] = useState<MapVersion[]>([]);
  const [selectedVersionId, setSelectedVersionId] = useState<string | null>(null);

  const [objects, setObjects] = useState<EditorObject[]>([]);
  const [selectedObjectId, setSelectedObjectId] = useState<string | null>(null);
  const [history, setHistory] = useState<EditorObject[][]>([]);
  const [redo, setRedo] = useState<EditorObject[][]>([]);
  const [tool, setTool] = useState<ToolMode>('select');
  const [draftPoints, setDraftPoints] = useState<RoutePoint[]>([]);

  const [terrainTypes, setTerrainTypes] = useState<TerrainType[]>([]);
  const [graph, setGraph] = useState<RouteGraph | null>(null);

  const [routePoints, setRoutePoints] = useState<RoutePoint[]>([]);
  const [routeVariants, setRouteVariants] = useState<RouteVariant[]>([]);
  const [selectedRouteRank, setSelectedRouteRank] = useState(1);
  const [routeProgress, setRouteProgress] = useState(0);
  const [routeStatus, setRouteStatus] = useState<'idle' | 'in-progress' | 'completed' | 'failed'>('idle');
  const [routeHistory, setRouteHistory] = useState<RouteRun[]>([]);

  const [digitize, setDigitize] = useState<DigitizationJob | null>(null);
  const [error, setError] = useState('');
  const [uploading, setUploading] = useState(false);
  const [uploadingOcd, setUploadingOcd] = useState(false);
  const [saving, setSaving] = useState(false);
  const [activeTab, setActiveTab] = useState<MapsTab>('ingest');
  const [typeModalOpen, setTypeModalOpen] = useState(false);
  const [editingTypeId, setEditingTypeId] = useState<string | null>(null);
  const [typeFormName, setTypeFormName] = useState('');
  const [typeFormColor, setTypeFormColor] = useState('#22c55e');
  const [typeFormTraversability, setTypeFormTraversability] = useState(50);
  const [typeFormClass, setTypeFormClass] = useState<TerrainClass>('ManMade');
  const [typeFormComment, setTypeFormComment] = useState('');

  const [image, setImage] = useState<HTMLImageElement | null>(null);
  const [scale, setScale] = useState(1);
  const [stagePos, setStagePos] = useState<RoutePoint>({ x: 0, y: 0 });
  const [layerSource, setLayerSource] = useState(true);
  const [layerDigitized, setLayerDigitized] = useState(true);
  const [layerGraph, setLayerGraph] = useState(false);
  const [layerRoute, setLayerRoute] = useState(true);

  const selectedRoute = useMemo(
    () => routeVariants.find((x) => x.rank === selectedRouteRank) ?? routeVariants[0] ?? null,
    [routeVariants, selectedRouteRank],
  );

  const selectedObject = useMemo(
    () => objects.find((x) => x.id === selectedObjectId) ?? null,
    [objects, selectedObjectId],
  );

  const terrainTypeById = useMemo(
    () => new Map(terrainTypes.map((type) => [type.id, type])),
    [terrainTypes],
  );
  const terrainTypesForSelectedClass = useMemo(
    () => terrainTypes.filter((t) => t.terrainClass === selectedObject?.terrainClass),
    [terrainTypes, selectedObject?.terrainClass],
  );

  const graphNodeById = useMemo(
    () => new Map((graph?.nodes ?? []).map((node) => [node.id, { x: node.x, y: node.y }])),
    [graph],
  );

  const graphEdgeLines = useMemo<GraphEdgeLine[]>(() => {
    if (!graph) return [];
    return graph.edges
      .map((edge) => {
        const from = graphNodeById.get(edge.fromNodeId);
        const to = graphNodeById.get(edge.toNodeId);
        if (!from || !to) return null;
        return { from, to, weight: edge.weight };
      })
      .filter((item): item is GraphEdgeLine => item !== null);
  }, [graph, graphNodeById]);

  const digitizedPoints = useMemo<RoutePoint[]>(
    () => objects.flatMap((obj) => obj.points),
    [objects],
  );

  function parseObject(item: TerrainObject): EditorObject | null {
    try {
      const parsed = JSON.parse(item.geometryJson) as { x?: number; y?: number; points?: RoutePoint[] };
      if (item.geometryKind === 'Point' && parsed.x !== undefined && parsed.y !== undefined) {
        return {
          id: item.id,
          geometryKind: 'Point',
          terrainClass: item.terrainClass,
          terrainObjectTypeId: item.terrainObjectTypeId,
          traversability: item.traversability,
          points: [{ x: parsed.x, y: parsed.y }],
          source: item.source,
        };
      }

      if ((item.geometryKind === 'Line' || item.geometryKind === 'Polygon') && parsed.points?.length) {
        return {
          id: item.id,
          geometryKind: item.geometryKind,
          terrainClass: item.terrainClass,
          terrainObjectTypeId: item.terrainObjectTypeId,
          traversability: item.traversability,
          points: parsed.points,
          source: item.source,
        };
      }
    } catch {
      return null;
    }

    return null;
  }

  function pushHistory(next: EditorObject[]) {
    setHistory((prev) => [...prev, objects]);
    setRedo([]);
    setObjects(next);
  }

  function resolveObjectColor(obj: EditorObject) {
    if (obj.terrainObjectTypeId) {
      const type = terrainTypeById.get(obj.terrainObjectTypeId);
      if (type?.color) return type.color;
    }

    return OBJECT_COLORS[obj.terrainClass];
  }

  function worldToCanvas(point: RoutePoint): RoutePoint {
    return { x: point.x, y: -point.y };
  }

  function canvasToWorld(point: RoutePoint): RoutePoint {
    return { x: point.x, y: -point.y };
  }

  function fitViewportToObjects(items: EditorObject[]) {
    if (items.length === 0) {
      setScale(1);
      setStagePos({ x: 0, y: 0 });
      return;
    }

    const allPoints = items.flatMap((x) => x.points);
    if (allPoints.length === 0) {
      return;
    }

    let minX = allPoints[0].x;
    let minY = allPoints[0].y;
    let maxX = allPoints[0].x;
    let maxY = allPoints[0].y;
    for (const p of allPoints) {
      if (p.x < minX) minX = p.x;
      if (p.y < minY) minY = p.y;
      if (p.x > maxX) maxX = p.x;
      if (p.y > maxY) maxY = p.y;
    }

    const centerX = (minX + maxX) / 2;
    const centerY = (minY + maxY) / 2;
    const displayCenter = worldToCanvas({ x: centerX, y: centerY });
    const offsetX = CANVAS_WIDTH / 2 - displayCenter.x;
    const offsetY = CANVAS_HEIGHT / 2 - displayCenter.y;

    setScale(1);
    setStagePos({ x: offsetX, y: offsetY });
  }

  function snapPointToDigitizedPoint(point: RoutePoint): RoutePoint | null {
    if (!digitizedPoints.length) return null;

    const maxDistanceInWorld = 12 / Math.max(0.01, scale);
    const maxDistanceSq = maxDistanceInWorld * maxDistanceInWorld;
    let best: RoutePoint | null = null;
    let bestDistance = Number.POSITIVE_INFINITY;
    for (const candidate of digitizedPoints) {
      const dx = candidate.x - point.x;
      const dy = candidate.y - point.y;
      const distance = dx * dx + dy * dy;
      if (distance < bestDistance) {
        bestDistance = distance;
        best = candidate;
      }
    }

    if (!best || bestDistance > maxDistanceSq) {
      return null;
    }

    return { x: best.x, y: best.y };
  }

  function toolButtonStyle(mode: ToolMode) {
    const active = tool === mode;
    return {
      background: active ? '#111827' : undefined,
      color: active ? '#fff' : undefined,
      borderColor: active ? '#111827' : undefined,
      fontWeight: active ? 700 : 500,
    } as const;
  }

  async function loadGraph(mapId: string, versionId: string | null) {
    if (!versionId) {
      setGraph(null);
      return;
    }

    const routeGraph = await getRouteGraph(mapId, versionId, { timeWeight: 0.6, safetyWeight: 0.4 });
    setGraph(routeGraph);
  }

  async function loadMaps() {
    const list = await getMaps();
    setMaps(list);
    if (!selectedMapId && list[0]) {
      setSelectedMapId(list[0].id);
    }
  }

  async function loadMapData(mapId: string) {
    const [details, mapVersions, types] = await Promise.all([getMap(mapId), getMapVersions(mapId), getTerrainTypes()]);
    setMapDetails(details);
    setVersions(mapVersions);
    setTerrainTypes(types);
    const versionId = details.activeVersionId ?? mapVersions[0]?.id ?? null;
    setSelectedVersionId(versionId);

    if (versionId) {
      const terrain = await getTerrainObjects(mapId, versionId);
      const parsed = terrain.map(parseObject).filter((x): x is EditorObject => x !== null);
      setObjects(parsed);
      fitViewportToObjects(parsed);
    } else {
      setObjects([]);
      fitViewportToObjects([]);
    }
    await loadGraph(mapId, versionId);

    setImage(null);
    const url = await getMapImageObjectUrl(mapId);
    const img = new window.Image();
    img.onload = () => setImage(img);
    img.onerror = () => setImage(null);
    img.src = url;

    setHistory([]);
    setRedo([]);
    setRoutePoints([]);
    setRouteVariants([]);
    setSelectedRouteRank(1);
  }

  useEffect(() => {
    void loadMaps().catch((e: Error) => setError(e.message));
  }, []);

  useEffect(() => {
    if (!selectedMapId) return;
    void loadMapData(selectedMapId).catch((e: Error) => setError(e.message));
  }, [selectedMapId]);

  useEffect(() => {
    if (!selectedMapId || !selectedVersionId) return;
    void Promise.all([
      getTerrainObjects(selectedMapId, selectedVersionId),
      getRouteGraph(selectedMapId, selectedVersionId, { timeWeight: 0.6, safetyWeight: 0.4 }),
    ])
      .then(([terrain, routeGraph]) => {
        const parsed = terrain.map(parseObject).filter((x): x is EditorObject => x !== null);
        setObjects(parsed);
        fitViewportToObjects(parsed);
        setGraph(routeGraph);
        setHistory([]);
        setRedo([]);
        setRoutePoints([]);
        setRouteVariants([]);
      })
      .catch((e: Error) => setError(e.message));
  }, [selectedVersionId]);

  useEffect(() => {
    if (activeTab === 'routes') {
      setTool('route-point');
      return;
    }

    if (tool === 'route-point') {
      setTool('select');
    }
  }, [activeTab]);

  async function onUpload(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    if (!file) return;
    setUploading(true);
    try {
      await uploadMap(file);
      await loadMaps();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Ошибка загрузки');
    } finally {
      setUploading(false);
      event.target.value = '';
    }
  }

  async function onUploadOcd(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    if (!file) return;
    setUploadingOcd(true);
    try {
      await uploadOcdMap(file);
      await loadMaps();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Ошибка загрузки OCAD');
    } finally {
      setUploadingOcd(false);
      event.target.value = '';
    }
  }

  function canvasPointFromEvent(event: Konva.KonvaEventObject<MouseEvent | TouchEvent>) {
    const stage = event.target.getStage();
    const pointer = stage?.getPointerPosition();
    if (!pointer) return null;
    return canvasToWorld({
      x: (pointer.x - stagePos.x) / scale,
      y: (pointer.y - stagePos.y) / scale,
    });
  }

  function onStageClick(event: Konva.KonvaEventObject<MouseEvent | TouchEvent>) {
    const p = canvasPointFromEvent(event);
    if (!p) return;

    if (activeTab === 'routes') {
      if (tool !== 'route-point') return;
      const snapped = snapPointToDigitizedPoint(p);
      if (!snapped) {
        setError('Точка маршрута должна выбираться из существующих оцифрованных вершин.');
        return;
      }

      setRoutePoints((prev) => {
        const last = prev[prev.length - 1];
        if (last && Math.abs(last.x - snapped.x) < 0.0001 && Math.abs(last.y - snapped.y) < 0.0001) {
          return prev;
        }

        return [...prev, snapped];
      });
      return;
    }

    if (tool === 'route-point') return;

    if (tool === 'point') {
      pushHistory([
        ...objects,
        {
          id: crypto.randomUUID(),
          geometryKind: 'Point',
          terrainClass: 'Relief',
          terrainObjectTypeId: null,
          traversability: 50,
          points: [p],
          source: 'Manual',
        },
      ]);
      return;
    }

    if (tool === 'line' || tool === 'polygon') {
      setDraftPoints((prev) => [...prev, p]);
      return;
    }

    if (event.target === event.target.getStage()) {
      setSelectedObjectId(null);
    }
  }

  function finishDraftShape() {
    if (tool === 'line' && draftPoints.length < 2) return;
    if (tool === 'polygon' && draftPoints.length < 3) return;
    if (tool !== 'line' && tool !== 'polygon') return;

    pushHistory([
      ...objects,
      {
        id: crypto.randomUUID(),
        geometryKind: tool === 'line' ? 'Line' : 'Polygon',
        terrainClass: 'Relief',
        terrainObjectTypeId: null,
        traversability: 50,
        points: draftPoints,
        source: 'Manual',
      },
    ]);
    setDraftPoints([]);
  }

  function updateObject(id: string, updater: (x: EditorObject) => EditorObject) {
    pushHistory(objects.map((obj) => (obj.id === id ? updater(obj) : obj)));
  }

  function undo() {
    if (!history.length) return;
    const prev = history[history.length - 1];
    setHistory((h) => h.slice(0, -1));
    setRedo((r) => [...r, objects]);
    setObjects(prev);
  }

  function redoAction() {
    if (!redo.length) return;
    const next = redo[redo.length - 1];
    setRedo((r) => r.slice(0, -1));
    setHistory((h) => [...h, objects]);
    setObjects(next);
  }

  async function runDigitization() {
    if (!selectedMapId || !selectedVersionId) return;
    const start = await startDigitization(selectedMapId, selectedVersionId);
    const timer = window.setInterval(async () => {
      const status = await getDigitizationStatus(selectedMapId, start.jobId);
      setDigitize(status);
      if (status.status === 'Completed' || status.status === 'Failed') {
        window.clearInterval(timer);
        await loadMapData(selectedMapId);
      }
    }, 900);
  }

  async function saveVersion() {
    if (!selectedMapId || !selectedVersionId) return;
    setSaving(true);
    try {
      await saveTerrainObjects(
        selectedMapId,
        objects.map((x) => ({
          id: x.id,
          terrainClass: x.terrainClass,
          terrainObjectTypeId: x.terrainObjectTypeId,
          geometryKind: x.geometryKind,
          geometryJson:
            x.geometryKind === 'Point'
              ? JSON.stringify({ x: x.points[0].x, y: x.points[0].y })
              : JSON.stringify({ points: x.points }),
          traversability: x.traversability,
        })),
        selectedVersionId,
        'Правки через редактор',
      );
      await loadMapData(selectedMapId);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Ошибка сохранения версии');
    } finally {
      setSaving(false);
    }
  }

  async function runRouting() {
    if (!selectedMapId || !selectedVersionId || routePoints.length < 2) return;
    setRouteStatus('in-progress');
    setRouteProgress(0);
    try {
      const started = await calculateRoutes(selectedMapId, routePoints, { timeWeight: 0.6, safetyWeight: 0.4 }, selectedVersionId);
      const timer = window.setInterval(async () => {
        const status = await getRouteJobStatus(started.jobId);
        setRouteStatus(status.status);
        setRouteProgress(status.progress);
        if (status.result?.routes) {
          setRouteVariants(status.result.routes);
        }

        if (status.status === 'completed' || status.status === 'failed') {
          window.clearInterval(timer);
          if (status.result?.routes) {
            const routes = status.result.routes;
            setRouteHistory((prev) => [
              { id: crypto.randomUUID(), createdAt: new Date().toISOString(), points: routePoints, routes },
              ...prev,
            ]);
          }
        }
      }, 900);
    } catch (e) {
      setRouteStatus('failed');
      setError(e instanceof Error ? e.message : 'Ошибка маршрутизации');
    }
  }

  function exportCurrentRoute() {
    if (!selectedRoute) return;
    const blob = new Blob([JSON.stringify(selectedRoute, null, 2)], { type: 'application/json;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `route-${selectedRoute.rank}.json`;
    a.click();
    URL.revokeObjectURL(url);
  }

  function resetTypeForm() {
    setTypeFormName('');
    setTypeFormColor('#22c55e');
    setTypeFormTraversability(50);
    setTypeFormClass('ManMade');
    setTypeFormComment('');
  }

  function openCreateTypeModal() {
    setEditingTypeId(null);
    resetTypeForm();
    setTypeModalOpen(true);
  }

  function openEditTypeModal(type: TerrainType) {
    setEditingTypeId(type.id);
    setTypeFormName(type.name);
    setTypeFormColor(type.color);
    setTypeFormTraversability(type.traversability);
    setTypeFormClass(type.terrainClass);
    setTypeFormComment(type.comment ?? '');
    setTypeModalOpen(true);
  }

  function closeTypeModal() {
    setTypeModalOpen(false);
    setEditingTypeId(null);
  }

  async function saveTerrainTypeFromModal() {
    const name = typeFormName.trim();
    if (!name) {
      setError('Введите название пользовательского типа');
      return;
    }

    if (Number.isNaN(typeFormTraversability) || typeFormTraversability < 0 || typeFormTraversability > 100) {
      setError('Проходимость должна быть в диапазоне 0-100');
      return;
    }

    if (!editingTypeId) {
      try {
        const created = await createTerrainType({
          terrainClass: typeFormClass,
          name,
          color: typeFormColor,
          icon: 'custom',
          traversability: typeFormTraversability,
          comment: typeFormComment,
        });
        setTerrainTypes((prev) => [...prev, created]);
        closeTypeModal();
        resetTypeForm();
      } catch (e) {
        setError(e instanceof Error ? e.message : 'Ошибка создания типа');
      }
      return;
    }

    const existingType = terrainTypes.find((x) => x.id === editingTypeId);
    if (!existingType) {
      setError('Тип не найден');
      return;
    }

    try {
      const updated = await updateTerrainType(existingType.id, {
        terrainClass: typeFormClass,
        name,
        color: typeFormColor,
        icon: existingType.icon,
        traversability: typeFormTraversability,
        comment: typeFormComment,
      });
      setTerrainTypes((prev) => prev.map((x) => (x.id === updated.id ? updated : x)));
      setObjects((prev) =>
        prev.map((obj) =>
          obj.terrainObjectTypeId === updated.id
            ? { ...obj, traversability: updated.traversability }
            : obj,
        ),
      );
      closeTypeModal();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Ошибка обновления типа');
    }
  }

  async function removeTerrainType(id: string) {
    try {
      await deleteTerrainType(id);
      setTerrainTypes((prev) => prev.filter((x) => x.id !== id));
      setObjects((prev) =>
        prev.map((obj) =>
          obj.terrainObjectTypeId === id ? { ...obj, terrainObjectTypeId: null } : obj,
        ),
      );
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Ошибка удаления типа');
    }
  }

  return (
    <div className="page maps maps-wave2">
      <header className="topbar">
        <h1>Карты и маршруты</h1>
        <button onClick={() => { clearAuth(); navigate('/login'); }}>Выйти</button>
      </header>

      <section className="panel selected-map-banner">
        <h2>{mapDetails?.name ?? 'Карта не выбрана'}</h2>
      </section>

      <section className="panel tabs-shell">
        <div className="tabs-row">
          <button className={activeTab === 'ingest' ? 'active-tab' : ''} onClick={() => setActiveTab('ingest')}>Загрузка и оцифровка</button>
          <button className={activeTab === 'editor' ? 'active-tab' : ''} onClick={() => setActiveTab('editor')}>Редактор карты</button>
          <button className={activeTab === 'routes' ? 'active-tab' : ''} onClick={() => setActiveTab('routes')}>Маршруты</button>
        </div>
      </section>

      {activeTab === 'ingest' && (
        <section className="panel tab-content-stack">
          <div className="upload-grid">
            <div className="upload-block">
              <h3>Загрузка PNG/JPEG</h3>
              <label className="upload">
                <span>{uploading ? 'Загрузка...' : 'Загрузить карту PNG/JPEG'}</span>
                <input type="file" accept="image/png,image/jpeg" onChange={onUpload} />
              </label>
            </div>
            <div className="upload-block">
              <h3>Импорт OCAD</h3>
              <label className="upload">
                <span>{uploadingOcd ? 'Импорт OCAD...' : 'Импорт карты OCAD (.ocd)'}</span>
                <input type="file" accept=".ocd,application/octet-stream" onChange={onUploadOcd} />
              </label>
            </div>
            <div className="upload-block">
              <h3>Оцифровка</h3>
              <label>Версия
                <select value={selectedVersionId ?? ''} onChange={(e) => setSelectedVersionId(e.target.value)}>
                  {versions.map((v) => <option key={v.id} value={v.id}>v{v.versionNumber} ({v.notes})</option>)}
                </select>
              </label>
              <button onClick={runDigitization} disabled={!selectedMapId || !selectedVersionId}>Оцифровать</button>
              {digitize && <p className="hint">Статус: {digitize.status} ({digitize.progress}%)</p>}
            </div>
          </div>

          <div className="upload-block">
            <h3>Список карт</h3>
            <table><thead><tr><th>Название</th><th>Статус</th><th>Дата</th></tr></thead><tbody>
              {maps.map((m) => (
                <tr key={m.id} className={selectedMapId === m.id ? 'selected-row' : ''} onClick={() => setSelectedMapId(m.id)}>
                  <td>{m.name}</td><td>{m.status}</td><td>{new Date(m.createdAtUtc).toLocaleString()}</td>
                </tr>
              ))}
            </tbody></table>
          </div>
        </section>
      )}

      {(activeTab === 'editor' || activeTab === 'routes') && (
        <section className="panel editor-shell">
          <div className="toolbar-group">
            {activeTab === 'editor' && (
              <>
                <button style={toolButtonStyle('select')} onClick={() => setTool('select')}>Выбор</button>
                <button style={toolButtonStyle('point')} onClick={() => setTool('point')}>Точка</button>
                <button style={toolButtonStyle('line')} onClick={() => setTool('line')}>Линия</button>
                <button style={toolButtonStyle('polygon')} onClick={() => setTool('polygon')}>Полигон</button>
                <button onClick={finishDraftShape} disabled={draftPoints.length === 0}>Завершить фигуру</button>
                <button onClick={() => setDraftPoints([])} disabled={draftPoints.length === 0}>Очистить</button>
                <button onClick={undo} disabled={!history.length}>Undo</button>
                <button onClick={redoAction} disabled={!redo.length}>Redo</button>
                <button onClick={saveVersion} disabled={saving}>{saving ? 'Сохраняем...' : 'Сохранить версию'}</button>
              </>
            )}
            {activeTab === 'routes' && (
              <>
                <button style={toolButtonStyle('route-point')} onClick={() => setTool('route-point')}>Точки маршрута</button>
                <button onClick={runRouting} disabled={routePoints.length < 2}>Рассчитать top-3</button>
                <button onClick={() => setRoutePoints([])}>Очистить точки</button>
                <button onClick={exportCurrentRoute} disabled={!selectedRoute}>Экспорт маршрута</button>
              </>
            )}
          </div>

          <div className="toolbar-group">
            <label>Версия
              <select value={selectedVersionId ?? ''} onChange={(e) => setSelectedVersionId(e.target.value)}>
                {versions.map((v) => <option key={v.id} value={v.id}>v{v.versionNumber} ({v.notes})</option>)}
              </select>
            </label>
            <label><input type="checkbox" checked={layerSource} onChange={(e) => setLayerSource(e.target.checked)} /> исходник</label>
            <label><input type="checkbox" checked={layerDigitized} onChange={(e) => setLayerDigitized(e.target.checked)} /> оцифровка</label>
            {activeTab === 'editor' && <label><input type="checkbox" checked={layerGraph} onChange={(e) => setLayerGraph(e.target.checked)} /> граф</label>}
            <label><input type="checkbox" checked={layerRoute} onChange={(e) => setLayerRoute(e.target.checked)} /> маршрут</label>
          </div>

          <div className="editor-main">
            <div className="canvas-panel">
              <div className="layer-switcher">
                <span>Масштаб: {Math.round(scale * 100)}%</span>
                <button onClick={() => setScale((s) => Math.max(0.2, s - 0.1))}>-</button>
                <button onClick={() => setScale((s) => Math.min(5, s + 0.1))}>+</button>
              </div>
              <Stage
                width={CANVAS_WIDTH}
                height={CANVAS_HEIGHT}
                x={stagePos.x}
                y={stagePos.y}
                scale={{ x: scale, y: scale }}
                draggable
                onDragEnd={(e) => setStagePos({ x: e.target.x(), y: e.target.y() })}
                onClick={onStageClick}
                onTap={onStageClick}
              >
                {layerSource && <Layer>{image && <KonvaImage image={image} width={CANVAS_WIDTH} height={CANVAS_HEIGHT} opacity={0.9} />}</Layer>}
                {layerDigitized && (
                  <Layer>
                    {objects.map((obj) => {
                      const color = resolveObjectColor(obj);
                      if (obj.geometryKind === 'Point') {
                        const canvasPoint = worldToCanvas(obj.points[0] ?? { x: 0, y: 0 });
                        return (
                          <Circle
                            key={obj.id}
                            x={canvasPoint.x}
                            y={canvasPoint.y}
                            radius={obj.id === selectedObjectId ? POINT_RADIUS_SELECTED : POINT_RADIUS}
                            fill={color}
                            draggable={activeTab === 'editor' && obj.id === selectedObjectId && tool === 'select'}
                            onClick={(e) => {
                              if (tool === 'route-point' || activeTab !== 'editor') return;
                              e.cancelBubble = true;
                              setSelectedObjectId(obj.id);
                            }}
                            onTap={(e) => {
                              if (tool === 'route-point' || activeTab !== 'editor') return;
                              e.cancelBubble = true;
                              setSelectedObjectId(obj.id);
                            }}
                            onDragEnd={(e) =>
                              updateObject(obj.id, (current) => ({
                                ...current,
                                points: [canvasToWorld({ x: e.target.x(), y: e.target.y() })],
                              }))
                            }
                          />
                        );
                      }

                      return (
                        <Line
                          key={obj.id}
                          points={obj.points.flatMap((p) => {
                            const canvasPoint = worldToCanvas(p);
                            return [canvasPoint.x, canvasPoint.y];
                          })}
                          closed={obj.geometryKind === 'Polygon'}
                          fill={obj.geometryKind === 'Polygon' ? `${color}44` : undefined}
                          stroke={color}
                          strokeWidth={obj.id === selectedObjectId ? OBJECT_STROKE_WIDTH_SELECTED : OBJECT_STROKE_WIDTH}
                          hitStrokeWidth={12}
                          onClick={(e) => {
                            if (tool === 'route-point' || activeTab !== 'editor') return;
                            e.cancelBubble = true;
                            setSelectedObjectId(obj.id);
                          }}
                          onTap={(e) => {
                            if (tool === 'route-point' || activeTab !== 'editor') return;
                            e.cancelBubble = true;
                            setSelectedObjectId(obj.id);
                          }}
                        />
                      );
                    })}

                    {activeTab === 'editor' &&
                      selectedObject?.geometryKind !== 'Point' &&
                      selectedObject?.points.map((p, i) => (
                        (() => {
                          const canvasPoint = worldToCanvas(p);
                          return (
                        <Circle
                          key={`${selectedObject.id}-v-${i}`}
                          x={canvasPoint.x}
                          y={canvasPoint.y}
                          radius={VERTEX_RADIUS}
                          fill="#fff"
                          stroke="#111"
                          draggable={tool === 'select'}
                          onDragEnd={(e) =>
                            updateObject(selectedObject.id, (current) => ({
                              ...current,
                              points: current.points.map((pt, idx) => (
                                idx === i ? canvasToWorld({ x: e.target.x(), y: e.target.y() }) : pt
                              )),
                            }))
                          }
                        />
                          );
                        })()
                      ))}

                    {activeTab === 'editor' && draftPoints.length > 0 && (
                      <Line
                        points={draftPoints.flatMap((p) => {
                          const canvasPoint = worldToCanvas(p);
                          return [canvasPoint.x, canvasPoint.y];
                        })}
                        stroke="#f97316"
                        dash={[7, 6]}
                        strokeWidth={1.3}
                      />
                    )}
                  </Layer>
                )}
                {activeTab === 'editor' && layerGraph && (
                  <Layer>
                    {graphEdgeLines.map((edge, index) => (
                      (() => {
                        const from = worldToCanvas(edge.from);
                        const to = worldToCanvas(edge.to);
                        return (
                      <Line
                        key={`g-e-${index}`}
                        points={[from.x, from.y, to.x, to.y]}
                        stroke="#60a5fa"
                        opacity={0.38}
                        strokeWidth={1}
                      />
                        );
                      })()
                    ))}
                    {(graph?.nodes ?? []).map((node) => (
                      (() => {
                        const canvasPoint = worldToCanvas({ x: node.x, y: node.y });
                        return (
                      <Circle
                        key={node.id}
                        x={canvasPoint.x}
                        y={canvasPoint.y}
                        radius={1.1}
                        fill="#93c5fd"
                        opacity={0.5}
                      />
                        );
                      })()
                    ))}
                  </Layer>
                )}
                {layerRoute && <Layer>
                  {routePoints.map((p, i) => {
                    const canvasPoint = worldToCanvas(p);
                    return <Circle key={`rp-${i}`} x={canvasPoint.x} y={canvasPoint.y} radius={POINT_RADIUS_SELECTED} fill="#f97316" />;
                  })}
                  {routeVariants.map((r, i) => (
                    <Line
                      key={r.rank}
                      points={r.polyline.flatMap((p) => {
                        const canvasPoint = worldToCanvas(p);
                        return [canvasPoint.x, canvasPoint.y];
                      })}
                      stroke={['#f43f5e', '#f97316', '#22c55e'][i]}
                      strokeWidth={selectedRoute?.rank === r.rank ? OBJECT_STROKE_WIDTH_SELECTED : OBJECT_STROKE_WIDTH}
                      opacity={selectedRoute?.rank === r.rank ? 1 : 0.6}
                    />
                  ))}
                </Layer>}
              </Stage>
            </div>

            {activeTab === 'editor' && (
              <aside className="side-panel">
                <h3>Свойства объекта</h3>
                {!selectedObject && <p>Объект не выбран</p>}
                {selectedObject && (
                  <div className="side-group">
                    <label>Класс
                      <select
                        value={selectedObject.terrainClass}
                        onChange={(e) =>
                          updateObject(selectedObject.id, (x) => {
                            const nextClass = e.target.value as TerrainClass;
                            const selectedType = x.terrainObjectTypeId ? terrainTypeById.get(x.terrainObjectTypeId) : null;
                            return {
                              ...x,
                              terrainClass: nextClass,
                              terrainObjectTypeId: selectedType && selectedType.terrainClass !== nextClass ? null : x.terrainObjectTypeId,
                            };
                          })
                        }
                      >
                        {CLASS_OPTIONS.map((c) => <option key={c} value={c}>{CLASS_LABELS_RU[c]}</option>)}
                      </select>
                    </label>
                    <label>Тип
                      <div className="types-icons-grid">
                        <button
                          type="button"
                          className={selectedObject.terrainObjectTypeId === null ? 'icon-chip active' : 'icon-chip'}
                          title="Тип не задан"
                          onClick={() =>
                            updateObject(selectedObject.id, (x) => ({
                              ...x,
                              terrainObjectTypeId: null,
                            }))
                          }
                        >
                          ∅
                        </button>
                        {terrainTypesForSelectedClass.map((t) => {
                          const imageUrl = getTerrainTypeImageUrl(t);
                          const isActive = selectedObject.terrainObjectTypeId === t.id;
                          return (
                            <button
                              key={t.id}
                              type="button"
                              className={isActive ? 'icon-chip active' : 'icon-chip'}
                              title={`${t.name} (${t.terrainClassNameRu})`}
                              onClick={() =>
                                updateObject(selectedObject.id, (x) => ({
                                  ...x,
                                  terrainClass: t.terrainClass,
                                  terrainObjectTypeId: t.id,
                                  traversability: t.traversability,
                                }))
                              }
                            >
                              {imageUrl ? (
                                <img src={imageUrl} alt={t.name} width={22} height={22} loading="lazy" />
                              ) : (
                                <span>{t.symbolCode || 'USR'}</span>
                              )}
                            </button>
                          );
                        })}
                      </div>
                    </label>
                    <label>Проходимость
                      <input type="number" min={0} max={100} step={1} value={selectedObject.traversability} onChange={(e) => updateObject(selectedObject.id, (x) => ({ ...x, traversability: Number(e.target.value) }))} />
                    </label>
                    <button onClick={() => pushHistory(objects.filter((x) => x.id !== selectedObject.id))}>Удалить объект</button>
                  </div>
                )}

                <h3>Типы объектов</h3>
                <div className="side-group">
                  <button onClick={openCreateTypeModal}>+ Добавить тип</button>
                  {terrainTypes.map((t) => (
                    <div key={t.id} className="types-actions">
                      <span
                        style={{ color: t.color, fontSize: 18, lineHeight: 1 }}
                        title={`${t.name} (${t.terrainClassNameRu}) • код ${t.symbolCode || 'USR'}`}
                      >
                        {getTerrainTypeImageUrl(t) ? (
                          <img src={getTerrainTypeImageUrl(t) ?? ''} alt={t.name} width={20} height={20} loading="lazy" />
                        ) : (
                          t.symbolCode || 'USR'
                        )}
                      </span>
                      {!t.isSystem && <button onClick={() => openEditTypeModal(t)}>Изм.</button>}
                      {!t.isSystem && <button onClick={() => removeTerrainType(t.id)}>Уд.</button>}
                    </div>
                  ))}
                </div>
              </aside>
            )}

            {activeTab === 'routes' && (
              <aside className="side-panel">
                <h3>Маршрутизация</h3>
                <p>Точек: {routePoints.length}</p>
                <p>Статус: {routeStatus}</p>
                <p>Прогресс: {routeProgress}%</p>
                <h3>Top-3</h3>
                {routeVariants.map((r) => <button key={r.rank} onClick={() => setSelectedRouteRank(r.rank)}>#{r.rank}: {r.totalCost.toFixed(1)}</button>)}
                {selectedRoute && (
                  <div className="side-group">
                    <p>Длина: {selectedRoute.length.toFixed(1)} м</p>
                    <p>Время: {selectedRoute.estimatedTime.toFixed(1)} сек</p>
                    <p>Штраф: {selectedRoute.penaltyScore.toFixed(2)}</p>
                    <h3>Почему этот маршрут</h3>
                    {selectedRoute.whyChosen.map((w, i) => <p key={i}>{w}</p>)}
                  </div>
                )}

                <h3>История запусков</h3>
                {routeHistory.length === 0 && <p>Пока пусто</p>}
                {routeHistory.map((run) => (
                  <button key={run.id} onClick={() => { setRoutePoints(run.points); setRouteVariants(run.routes); setSelectedRouteRank(1); }}>
                    {new Date(run.createdAt).toLocaleString()}
                  </button>
                ))}
              </aside>
            )}
          </div>
        </section>
      )}

      {typeModalOpen && (
        <div className="modal-backdrop" onClick={closeTypeModal}>
          <div className="modal-panel" onClick={(e) => e.stopPropagation()}>
            <h3>{editingTypeId ? 'Редактирование типа' : 'Новый тип'}</h3>
            <label>Название
              <input value={typeFormName} onChange={(e) => setTypeFormName(e.target.value)} placeholder="Например, Болото" />
            </label>
            <label>Класс
              <select value={typeFormClass} onChange={(e) => setTypeFormClass(e.target.value as TerrainClass)}>
                {CLASS_OPTIONS.map((c) => <option key={c} value={c}>{CLASS_LABELS_RU[c]}</option>)}
              </select>
            </label>
            <label>Цвет
              <input type="color" value={typeFormColor} onChange={(e) => setTypeFormColor(e.target.value)} />
            </label>
            <label>Проходимость
              <input
                type="number"
                min={0}
                max={100}
                step={1}
                value={typeFormTraversability}
                onChange={(e) => setTypeFormTraversability(Number(e.target.value))}
              />
            </label>
            <label>Комментарий
              <input value={typeFormComment} onChange={(e) => setTypeFormComment(e.target.value)} />
            </label>
            <div className="modal-actions">
              <button onClick={saveTerrainTypeFromModal}>Сохранить</button>
              <button onClick={closeTypeModal}>Отменить</button>
            </div>
          </div>
        </div>
      )}

      {error && <p className="error">{error}</p>}
    </div>
  );
}
