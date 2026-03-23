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
  getRouteJobStatus,
  getTerrainObjects,
  getTerrainTypes,
  saveTerrainObjects,
  startDigitization,
  updateTerrainType,
  uploadMap,
} from '../lib/api';
import type {
  DigitizationJob,
  MapDetails,
  MapListItem,
  MapVersion,
  RoutePoint,
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

type RouteRun = {
  id: string;
  createdAt: string;
  points: RoutePoint[];
  routes: RouteVariant[];
};

const CLASS_OPTIONS: TerrainClass[] = ['Vegetation', 'Water', 'Rock', 'Ground', 'ManMade'];
const OBJECT_COLORS: Record<TerrainClass, string> = {
  Vegetation: '#34D399',
  Water: '#60A5FA',
  Rock: '#9CA3AF',
  Ground: '#FBBF24',
  ManMade: '#FB7185',
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

  const [routePoints, setRoutePoints] = useState<RoutePoint[]>([]);
  const [routeVariants, setRouteVariants] = useState<RouteVariant[]>([]);
  const [selectedRouteRank, setSelectedRouteRank] = useState(1);
  const [routeProgress, setRouteProgress] = useState(0);
  const [routeStatus, setRouteStatus] = useState<'idle' | 'in-progress' | 'completed' | 'failed'>('idle');
  const [routeHistory, setRouteHistory] = useState<RouteRun[]>([]);

  const [digitize, setDigitize] = useState<DigitizationJob | null>(null);
  const [error, setError] = useState('');
  const [uploading, setUploading] = useState(false);
  const [saving, setSaving] = useState(false);

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
      setObjects(terrain.map(parseObject).filter((x): x is EditorObject => x !== null));
    } else {
      setObjects([]);
    }

    const url = await getMapImageObjectUrl(mapId);
    const img = new window.Image();
    img.onload = () => setImage(img);
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
    void getTerrainObjects(selectedMapId, selectedVersionId)
      .then((terrain) => {
        setObjects(terrain.map(parseObject).filter((x): x is EditorObject => x !== null));
        setHistory([]);
        setRedo([]);
      })
      .catch((e: Error) => setError(e.message));
  }, [selectedVersionId]);

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

  function canvasPointFromEvent(event: Konva.KonvaEventObject<MouseEvent>) {
    const stage = event.target.getStage();
    const pointer = stage?.getPointerPosition();
    if (!pointer) return null;
    return { x: (pointer.x - stagePos.x) / scale, y: (pointer.y - stagePos.y) / scale };
  }

  function onStageClick(event: Konva.KonvaEventObject<MouseEvent>) {
    const p = canvasPointFromEvent(event);
    if (!p) return;

    if (tool === 'route-point') {
      setRoutePoints((prev) => [...prev, p]);
      return;
    }

    if (tool === 'point') {
      pushHistory([
        ...objects,
        {
          id: crypto.randomUUID(),
          geometryKind: 'Point',
          terrainClass: 'Ground',
          terrainObjectTypeId: null,
          traversability: 1,
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
        terrainClass: 'Ground',
        terrainObjectTypeId: null,
        traversability: 1,
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

  async function addTerrainType() {
    try {
      const created = await createTerrainType({
        name: `Тип ${new Date().toLocaleTimeString()}`,
        color: '#d946ef',
        icon: 'custom',
        traversability: 1,
        comment: 'Пользовательский тип',
      });
      setTerrainTypes((prev) => [...prev, created]);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Ошибка создания типа');
    }
  }

  async function renameTerrainType(type: TerrainType) {
    try {
      const updated = await updateTerrainType(type.id, {
        name: `${type.name}*`,
        color: type.color,
        icon: type.icon,
        traversability: type.traversability,
        comment: type.comment,
      });
      setTerrainTypes((prev) => prev.map((x) => (x.id === updated.id ? updated : x)));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Ошибка обновления типа');
    }
  }

  async function removeTerrainType(id: string) {
    try {
      await deleteTerrainType(id);
      setTerrainTypes((prev) => prev.filter((x) => x.id !== id));
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

      <section className="panel">
        <label className="upload">
          <span>{uploading ? 'Загрузка...' : 'Загрузить карту PNG/JPEG'}</span>
          <input type="file" accept="image/png,image/jpeg" onChange={onUpload} />
        </label>
      </section>

      <section className="panel">
        <h2>Список карт</h2>
        <table><thead><tr><th>Название</th><th>Статус</th><th>Дата</th></tr></thead><tbody>
          {maps.map((m) => (
            <tr key={m.id} className={selectedMapId === m.id ? 'selected-row' : ''} onClick={() => setSelectedMapId(m.id)}>
              <td>{m.name}</td><td>{m.status}</td><td>{new Date(m.createdAtUtc).toLocaleString()}</td>
            </tr>
          ))}
        </tbody></table>
      </section>

      <section className="panel editor-shell">
        <div className="toolbar-group">
          <button onClick={() => setTool('select')}>Выбор</button>
          <button onClick={() => setTool('point')}>Точка</button>
          <button onClick={() => setTool('line')}>Линия</button>
          <button onClick={() => setTool('polygon')}>Полигон</button>
          <button onClick={() => setTool('route-point')}>Точки маршрута</button>
          <button onClick={finishDraftShape} disabled={draftPoints.length === 0}>Завершить фигуру</button>
          <button onClick={() => setDraftPoints([])} disabled={draftPoints.length === 0}>Очистить черновик</button>
          <button onClick={runDigitization}>Оцифровать</button>
          <button onClick={saveVersion} disabled={saving}>{saving ? 'Сохраняем...' : 'Сохранить версию'}</button>
          <button onClick={undo} disabled={!history.length}>Undo</button>
          <button onClick={redoAction} disabled={!redo.length}>Redo</button>
          <button onClick={runRouting} disabled={routePoints.length < 2}>Рассчитать top-3</button>
          <button onClick={() => setRoutePoints([])}>Очистить точки</button>
        </div>

        <div className="toolbar-group">
          <label>Версия
            <select value={selectedVersionId ?? ''} onChange={(e) => setSelectedVersionId(e.target.value)}>
              {versions.map((v) => <option key={v.id} value={v.id}>v{v.versionNumber} ({v.notes})</option>)}
            </select>
          </label>
          <label><input type="checkbox" checked={layerSource} onChange={(e) => setLayerSource(e.target.checked)} /> исходник</label>
          <label><input type="checkbox" checked={layerDigitized} onChange={(e) => setLayerDigitized(e.target.checked)} /> оцифровка</label>
          <label><input type="checkbox" checked={layerGraph} onChange={(e) => setLayerGraph(e.target.checked)} /> граф</label>
          <label><input type="checkbox" checked={layerRoute} onChange={(e) => setLayerRoute(e.target.checked)} /> маршрут</label>
        </div>

        <div className="editor-main">
          <div className="canvas-panel">
            <div className="layer-switcher">
              <span>Масштаб: {Math.round(scale * 100)}%</span>
              <button onClick={() => setScale((s) => Math.max(0.4, s - 0.1))}>-</button>
              <button onClick={() => setScale((s) => Math.min(2.5, s + 0.1))}>+</button>
            </div>
            <Stage
              width={900}
              height={560}
              x={stagePos.x}
              y={stagePos.y}
              scale={{ x: scale, y: scale }}
              draggable
              onDragEnd={(e) => setStagePos({ x: e.target.x(), y: e.target.y() })}
              onMouseDown={onStageClick}
            >
              {layerSource && <Layer>{image && <KonvaImage image={image} width={900} height={560} opacity={0.9} />}</Layer>}
              {layerDigitized && (
                <Layer>
                  {objects.map((obj) => {
                    const color = OBJECT_COLORS[obj.terrainClass];
                    if (obj.geometryKind === 'Point') {
                      return (
                        <Circle
                          key={obj.id}
                          x={obj.points[0]?.x ?? 0}
                          y={obj.points[0]?.y ?? 0}
                          radius={obj.id === selectedObjectId ? 8 : 6}
                          fill={color}
                          draggable={obj.id === selectedObjectId}
                          onClick={(e) => {
                            e.cancelBubble = true;
                            setSelectedObjectId(obj.id);
                          }}
                          onDragEnd={(e) =>
                            updateObject(obj.id, (current) => ({
                              ...current,
                              points: [{ x: e.target.x(), y: e.target.y() }],
                            }))
                          }
                        />
                      );
                    }

                    return (
                      <Line
                        key={obj.id}
                        points={obj.points.flatMap((p) => [p.x, p.y])}
                        closed={obj.geometryKind === 'Polygon'}
                        fill={obj.geometryKind === 'Polygon' ? `${color}44` : undefined}
                        stroke={color}
                        strokeWidth={obj.id === selectedObjectId ? 4 : 3}
                        onClick={(e) => {
                          e.cancelBubble = true;
                          setSelectedObjectId(obj.id);
                        }}
                      />
                    );
                  })}

                  {selectedObject?.geometryKind !== 'Point' &&
                    selectedObject?.points.map((p, i) => (
                      <Circle
                        key={`${selectedObject.id}-v-${i}`}
                        x={p.x}
                        y={p.y}
                        radius={5}
                        fill="#fff"
                        stroke="#111"
                        draggable
                        onDragEnd={(e) =>
                          updateObject(selectedObject.id, (current) => ({
                            ...current,
                            points: current.points.map((pt, idx) => (idx === i ? { x: e.target.x(), y: e.target.y() } : pt)),
                          }))
                        }
                      />
                    ))}

                  {draftPoints.length > 0 && (
                    <Line points={draftPoints.flatMap((p) => [p.x, p.y])} stroke="#f97316" dash={[7, 6]} strokeWidth={2} />
                  )}
                </Layer>
              )}
              {layerGraph && <Layer><Line points={[120, 140, 220, 180, 300, 160, 360, 230]} stroke="#a78bfa" dash={[6, 4]} /></Layer>}
              {layerRoute && <Layer>
                {routePoints.map((p, i) => <Circle key={`rp-${i}`} x={p.x} y={p.y} radius={6} fill="#f97316" />)}
                {routeVariants.map((r, i) => <Line key={r.rank} points={r.polyline.flatMap((p) => [p.x, p.y])} stroke={['#f43f5e', '#f97316', '#22c55e'][i]} strokeWidth={selectedRoute?.rank === r.rank ? 5 : 3} opacity={selectedRoute?.rank === r.rank ? 1 : 0.6} />)}
              </Layer>}
            </Stage>
          </div>

          <aside className="side-panel">
            <h3>Свойства объекта</h3>
            {!selectedObject && <p>Объект не выбран</p>}
            {selectedObject && (
              <div className="side-group">
                <label>Класс
                  <select value={selectedObject.terrainClass} onChange={(e) => updateObject(selectedObject.id, (x) => ({ ...x, terrainClass: e.target.value as TerrainClass }))}>
                    {CLASS_OPTIONS.map((c) => <option key={c} value={c}>{c}</option>)}
                  </select>
                </label>
                <label>Тип
                  <select value={selectedObject.terrainObjectTypeId ?? ''} onChange={(e) => updateObject(selectedObject.id, (x) => ({ ...x, terrainObjectTypeId: e.target.value || null }))}>
                    <option value="">Не задан</option>
                    {terrainTypes.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
                  </select>
                </label>
                <label>Проходимость
                  <input type="number" min={0.05} max={10} step={0.05} value={selectedObject.traversability} onChange={(e) => updateObject(selectedObject.id, (x) => ({ ...x, traversability: Number(e.target.value) }))} />
                </label>
                <button onClick={() => pushHistory(objects.filter((x) => x.id !== selectedObject.id))}>Удалить объект</button>
              </div>
            )}

            <h3>Типы местности</h3>
            <div className="side-group">
              <button onClick={addTerrainType}>Добавить тип</button>
              {terrainTypes.map((t) => (
                <div key={t.id} className="types-actions">
                  <span>{t.name}</span>
                  {!t.isSystem && <button onClick={() => renameTerrainType(t)}>Изм.</button>}
                  {!t.isSystem && <button onClick={() => removeTerrainType(t.id)}>Уд.</button>}
                </div>
              ))}
            </div>

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
                <h3>Легенда риска</h3>
                <p>Низкий: &lt;0.4 | Средний: 0.4-0.75 | Высокий: &gt;0.75</p>
                <button onClick={exportCurrentRoute}>Экспорт текущего маршрута</button>
              </div>
            )}

            <h3>История запусков</h3>
            {routeHistory.length === 0 && <p>Пока пусто</p>}
            {routeHistory.map((run) => (
              <button key={run.id} onClick={() => { setRoutePoints(run.points); setRouteVariants(run.routes); setSelectedRouteRank(1); }}>
                {new Date(run.createdAt).toLocaleString()}
              </button>
            ))}

            {digitize && <p>Оцифровка: {digitize.status} ({digitize.progress}%)</p>}
          </aside>
        </div>
      </section>

      {mapDetails && <p className="hint">Активная карта: {mapDetails.name}</p>}
      {error && <p className="error">{error}</p>}
    </div>
  );
}
