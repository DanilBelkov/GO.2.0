import { useEffect, useMemo, useState } from 'react';
import type { ChangeEvent } from 'react';
import { Circle, Image as KonvaImage, Layer, Line, Stage } from 'react-konva';
import type Konva from 'konva';
import { useNavigate } from 'react-router-dom';
import {
  createTerrainType,
  deleteTerrainType,
  getDigitizationStatus,
  getMap,
  getMapImageObjectUrl,
  getMapVersions,
  getMaps,
  getTerrainObjects,
  getTerrainTypes,
  saveTerrainObjects,
  startDigitization,
  updateTerrainType,
  uploadMap,
} from '../lib/api';
import { clearAuth } from '../lib/auth';
import type {
  DigitizationJob,
  MapDetails,
  MapListItem,
  MapVersion,
  TerrainClass,
  TerrainGeometryKind,
  TerrainObject,
  TerrainType,
} from '../lib/api';

type Point = { x: number; y: number };

type EditorObject = {
  id: string;
  terrainClass: TerrainClass;
  terrainObjectTypeId: string | null;
  geometryKind: TerrainGeometryKind;
  traversability: number;
  source: 'Auto' | 'Manual';
  points: Point[];
};

type ToolMode = 'select' | 'point' | 'line' | 'polygon';

const CLASS_OPTIONS: TerrainClass[] = ['Vegetation', 'Water', 'Rock', 'Ground', 'ManMade'];
const OBJECT_COLORS: Record<TerrainClass, string> = {
  Vegetation: '#34D399',
  Water: '#60A5FA',
  Rock: '#9CA3AF',
  Ground: '#FBBF24',
  ManMade: '#FB7185',
};

// Преобразует DTO API в внутреннюю модель редактора.
function parseObject(item: TerrainObject): EditorObject | null {
  try {
    const parsed = JSON.parse(item.geometryJson) as { x?: number; y?: number; points?: Point[] };
    if (item.geometryKind === 'Point' && typeof parsed.x === 'number' && typeof parsed.y === 'number') {
      return {
        id: item.id,
        terrainClass: item.terrainClass,
        terrainObjectTypeId: item.terrainObjectTypeId,
        geometryKind: item.geometryKind,
        traversability: item.traversability,
        source: item.source,
        points: [{ x: parsed.x, y: parsed.y }],
      };
    }

    if ((item.geometryKind === 'Line' || item.geometryKind === 'Polygon') && Array.isArray(parsed.points)) {
      return {
        id: item.id,
        terrainClass: item.terrainClass,
        terrainObjectTypeId: item.terrainObjectTypeId,
        geometryKind: item.geometryKind,
        traversability: item.traversability,
        source: item.source,
        points: parsed.points.map((p) => ({ x: p.x, y: p.y })),
      };
    }
  } catch {
    return null;
  }

  return null;
}

// Сериализует геометрию в формат, ожидаемый backend API.
function serializeGeometry(kind: TerrainGeometryKind, points: Point[]): string {
  if (kind === 'Point') {
    const [point] = points;
    return JSON.stringify({ x: point.x, y: point.y });
  }

  return JSON.stringify({ points });
}

// Главная рабочая страница: список карт + редактор оцифровки.
export function MapsPage() {
  const navigate = useNavigate();
  // Блок данных списка карт.
  const [maps, setMaps] = useState<MapListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [uploading, setUploading] = useState(false);

  // Блок выбранной карты и данных редактора.
  const [selectedMapId, setSelectedMapId] = useState<string | null>(null);
  const [mapDetails, setMapDetails] = useState<MapDetails | null>(null);
  const [versions, setVersions] = useState<MapVersion[]>([]);
  const [selectedVersionId, setSelectedVersionId] = useState<string | null>(null);
  const [terrainTypes, setTerrainTypes] = useState<TerrainType[]>([]);
  const [objects, setObjects] = useState<EditorObject[]>([]);
  const [selectedObjectId, setSelectedObjectId] = useState<string | null>(null);

  // Состояние инструментов редактора.
  const [toolMode, setToolMode] = useState<ToolMode>('select');
  const [draftPoints, setDraftPoints] = useState<Point[]>([]);
  const [history, setHistory] = useState<EditorObject[][]>([]);
  const [redoHistory, setRedoHistory] = useState<EditorObject[][]>([]);
  const [saving, setSaving] = useState(false);
  const [saveNote, setSaveNote] = useState('Manual edit');

  // Состояние canvas (подложка, масштаб, панорамирование).
  const [imageElement, setImageElement] = useState<HTMLImageElement | null>(null);
  const [stageScale, setStageScale] = useState(1);
  const [stagePosition, setStagePosition] = useState<Point>({ x: 0, y: 0 });

  // Layer switcher.
  const [showSourceLayer, setShowSourceLayer] = useState(true);
  const [showDigitizedLayer, setShowDigitizedLayer] = useState(true);
  const [showGraphLayer, setShowGraphLayer] = useState(false);
  const [showRouteLayer, setShowRouteLayer] = useState(false);

  // Состояние фоновой оцифровки и polling.
  const [digitizeState, setDigitizeState] = useState<DigitizationJob | null>(null);
  const [digitizing, setDigitizing] = useState(false);

  const selectedObject = useMemo(() => objects.find((x) => x.id === selectedObjectId) ?? null, [objects, selectedObjectId]);

  async function loadMaps() {
    setLoading(true);
    setError('');
    try {
      const data = await getMaps();
      setMaps(data);
      if (!selectedMapId && data.length > 0) {
        setSelectedMapId(data[0].id);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load maps');
    } finally {
      setLoading(false);
    }
  }

  async function loadEditorData(mapId: string, versionId?: string | null) {
    setError('');
    try {
      // Загружаем параллельно все ключевые данные для редактора.
      const [details, mapVersions, types] = await Promise.all([getMap(mapId), getMapVersions(mapId), getTerrainTypes()]);
      setMapDetails(details);
      setVersions(mapVersions);
      setTerrainTypes(types);

      const targetVersionId = versionId ?? details.activeVersionId ?? mapVersions[0]?.id ?? null;
      setSelectedVersionId(targetVersionId);

      const terrainObjects = await getTerrainObjects(mapId, targetVersionId ?? undefined);
      const parsedObjects = terrainObjects.map(parseObject).filter((x): x is EditorObject => x !== null);
      setObjects(parsedObjects);
      setHistory([]);
      setRedoHistory([]);
      setSelectedObjectId(null);

      // Загружаем исходник карты для слоя "source".
      const objectUrl = await getMapImageObjectUrl(mapId);
      const image = new window.Image();
      image.onload = () => setImageElement(image);
      image.src = objectUrl;
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load map editor');
    }
  }

  useEffect(() => {
    void loadMaps();
  }, []);

  useEffect(() => {
    if (selectedMapId) {
      void loadEditorData(selectedMapId);
    }
  }, [selectedMapId]);

  useEffect(() => {
    if (!selectedMapId || !selectedVersionId) {
      return;
    }

    void (async () => {
      try {
        const terrainObjects = await getTerrainObjects(selectedMapId, selectedVersionId);
        const parsedObjects = terrainObjects.map(parseObject).filter((x): x is EditorObject => x !== null);
        setObjects(parsedObjects);
        setHistory([]);
        setRedoHistory([]);
        setSelectedObjectId(null);
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to switch map version');
      }
    })();
  }, [selectedVersionId]);

  function onLogout() {
    clearAuth();
    navigate('/login');
  }

  async function onFileChange(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    if (!file) {
      return;
    }

    setUploading(true);
    setError('');
    try {
      await uploadMap(file);
      await loadMaps();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Upload failed');
    } finally {
      setUploading(false);
      event.target.value = '';
    }
  }

  function commitObjects(next: EditorObject[]) {
    // Любое изменение складываем в undo-стек и очищаем redo.
    setHistory((prev) => [...prev, objects]);
    setRedoHistory([]);
    setObjects(next);
  }

  function onUndo() {
    if (history.length === 0) {
      return;
    }

    const previous = history[history.length - 1];
    setHistory((prev) => prev.slice(0, -1));
    setRedoHistory((prev) => [...prev, objects]);
    setObjects(previous);
    setSelectedObjectId(null);
  }

  function onRedo() {
    if (redoHistory.length === 0) {
      return;
    }

    const next = redoHistory[redoHistory.length - 1];
    setRedoHistory((prev) => prev.slice(0, -1));
    setHistory((prev) => [...prev, objects]);
    setObjects(next);
    setSelectedObjectId(null);
  }

  function onStagePointerDown(event: Konva.KonvaEventObject<MouseEvent>) {
    if (!showDigitizedLayer) {
      return;
    }

    const stage = event.target.getStage();
    const pointer = stage?.getPointerPosition();
    if (!pointer) {
      return;
    }

    const x = (pointer.x - stagePosition.x) / stageScale;
    const y = (pointer.y - stagePosition.y) / stageScale;

    if (toolMode === 'select') {
      if (event.target === stage) {
        setSelectedObjectId(null);
      }

      return;
    }

    if (toolMode === 'point') {
      // Для точки объект создается одним кликом.
      const newObject: EditorObject = {
        id: crypto.randomUUID(),
        terrainClass: 'Ground',
        terrainObjectTypeId: null,
        geometryKind: 'Point',
        traversability: 1,
        source: 'Manual',
        points: [{ x, y }],
      };
      commitObjects([...objects, newObject]);
      setSelectedObjectId(newObject.id);
      return;
    }

    // Для линии/полигона накапливаем черновые вершины.
    setDraftPoints((prev) => [...prev, { x, y }]);
  }

  function onFinishDraft() {
    if (toolMode === 'line' && draftPoints.length < 2) {
      return;
    }

    if (toolMode === 'polygon' && draftPoints.length < 3) {
      return;
    }

    if (toolMode === 'line' || toolMode === 'polygon') {
      const newObject: EditorObject = {
        id: crypto.randomUUID(),
        terrainClass: 'Ground',
        terrainObjectTypeId: null,
        geometryKind: toolMode === 'line' ? 'Line' : 'Polygon',
        traversability: 1,
        source: 'Manual',
        points: draftPoints,
      };
      commitObjects([...objects, newObject]);
      setSelectedObjectId(newObject.id);
      setDraftPoints([]);
    }
  }

  function updateObject(id: string, updater: (current: EditorObject) => EditorObject) {
    const next = objects.map((obj) => (obj.id === id ? updater(obj) : obj));
    commitObjects(next);
  }

  function onDeleteObject() {
    if (!selectedObjectId) {
      return;
    }

    commitObjects(objects.filter((obj) => obj.id !== selectedObjectId));
    setSelectedObjectId(null);
  }

  async function onSaveObjects() {
    if (!selectedMapId || !selectedVersionId || objects.length === 0) {
      return;
    }

    setSaving(true);
    setError('');
    try {
      // Сохраняем все объекты как новую версию карты.
      await saveTerrainObjects(
        selectedMapId,
        objects.map((obj) => ({
          id: obj.id,
          terrainClass: obj.terrainClass,
          terrainObjectTypeId: obj.terrainObjectTypeId,
          geometryKind: obj.geometryKind,
          geometryJson: serializeGeometry(obj.geometryKind, obj.points),
          traversability: obj.traversability,
        })),
        selectedVersionId,
        saveNote,
      );

      await loadEditorData(selectedMapId);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save objects');
    } finally {
      setSaving(false);
    }
  }

  async function runDigitization() {
    if (!selectedMapId || !selectedVersionId || digitizing) {
      return;
    }

    setDigitizing(true);
    setError('');
    setDigitizeState(null);

    try {
      // Стартуем job и опрашиваем статус до завершения.
      const start = await startDigitization(selectedMapId, selectedVersionId);
      const interval = window.setInterval(async () => {
        try {
          const status = await getDigitizationStatus(selectedMapId, start.jobId);
          setDigitizeState(status);
          if (status.status === 'Completed' || status.status === 'Failed') {
            window.clearInterval(interval);
            setDigitizing(false);
            await loadEditorData(selectedMapId, status.mapVersionId);
          }
        } catch (err) {
          window.clearInterval(interval);
          setDigitizing(false);
          setError(err instanceof Error ? err.message : 'Polling failed');
        }
      }, 1000);
    } catch (err) {
      setDigitizing(false);
      setError(err instanceof Error ? err.message : 'Failed to start digitization');
    }
  }

  async function onCreateTerrainType() {
    try {
      await createTerrainType({
        name: `Custom ${new Date().toLocaleTimeString()}`,
        color: '#D946EF',
        icon: 'custom',
        traversability: 1,
        comment: 'User type',
      });
      const list = await getTerrainTypes();
      setTerrainTypes(list);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create type');
    }
  }

  async function onUpdateTerrainType(type: TerrainType) {
    try {
      await updateTerrainType(type.id, {
        name: `${type.name}*`,
        color: type.color,
        icon: type.icon,
        traversability: type.traversability,
        comment: type.comment,
      });
      const list = await getTerrainTypes();
      setTerrainTypes(list);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to update type');
    }
  }

  async function onDeleteTerrainType(id: string) {
    try {
      await deleteTerrainType(id);
      const list = await getTerrainTypes();
      setTerrainTypes(list);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete type');
    }
  }

  const draftLinePoints = draftPoints.flatMap((p) => [p.x, p.y]);

  return (
    <div className="page maps maps-wave2">
      <header className="topbar">
        <h1>Maps</h1>
        <button onClick={onLogout}>Logout</button>
      </header>

      <section className="panel">
        <label className="upload">
          <span>{uploading ? 'Uploading...' : 'Upload PNG/JPEG'}</span>
          <input type="file" accept="image/png,image/jpeg" onChange={onFileChange} disabled={uploading} />
        </label>
      </section>

      <section className="panel">
        <h2>Your maps</h2>
        {loading && <p>Loading...</p>}
        {!loading && maps.length === 0 && <p>Empty. Upload first map.</p>}
        {!loading && maps.length > 0 && (
          <table>
            <thead>
              <tr>
                <th>Name</th>
                <th>Status</th>
                <th>Created</th>
              </tr>
            </thead>
            <tbody>
              {maps.map((map) => (
                <tr
                  key={map.id}
                  className={selectedMapId === map.id ? 'selected-row' : ''}
                  onClick={() => setSelectedMapId(map.id)}
                >
                  <td>{map.name}</td>
                  <td>{map.status}</td>
                  <td>{new Date(map.createdAtUtc).toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      {selectedMapId && (
        <section className="panel editor-shell">
          <div className="editor-toolbar">
            <div className="toolbar-group">
              <button onClick={runDigitization} disabled={digitizing}>
                {digitizing ? 'Digitizing...' : 'Start digitization'}
              </button>
              <button onClick={onUndo} disabled={history.length === 0}>
                Undo
              </button>
              <button onClick={onRedo} disabled={redoHistory.length === 0}>
                Redo
              </button>
              <button onClick={onSaveObjects} disabled={saving || objects.length === 0}>
                {saving ? 'Saving...' : 'Save as new version'}
              </button>
            </div>

            <div className="toolbar-group">
              <button className={toolMode === 'select' ? 'active' : ''} onClick={() => setToolMode('select')}>
                Select
              </button>
              <button className={toolMode === 'point' ? 'active' : ''} onClick={() => setToolMode('point')}>
                Point
              </button>
              <button className={toolMode === 'line' ? 'active' : ''} onClick={() => setToolMode('line')}>
                Line
              </button>
              <button className={toolMode === 'polygon' ? 'active' : ''} onClick={() => setToolMode('polygon')}>
                Polygon
              </button>
              <button onClick={onFinishDraft} disabled={draftPoints.length === 0}>
                Finish shape
              </button>
              <button onClick={() => setDraftPoints([])} disabled={draftPoints.length === 0}>
                Clear draft
              </button>
            </div>

            <div className="toolbar-group">
              <label>
                Version
                <select
                  value={selectedVersionId ?? ''}
                  onChange={(event) => setSelectedVersionId(event.target.value || null)}
                >
                  {versions.map((version) => (
                    <option key={version.id} value={version.id}>
                      v{version.versionNumber} ({version.notes})
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Save note
                <input value={saveNote} onChange={(event) => setSaveNote(event.target.value)} />
              </label>
            </div>
          </div>

          <div className="editor-main">
            <div className="canvas-panel">
              <div className="layer-switcher">
                <label>
                  <input type="checkbox" checked={showSourceLayer} onChange={(e) => setShowSourceLayer(e.target.checked)} />
                  source
                </label>
                <label>
                  <input
                    type="checkbox"
                    checked={showDigitizedLayer}
                    onChange={(e) => setShowDigitizedLayer(e.target.checked)}
                  />
                  digitized
                </label>
                <label>
                  <input type="checkbox" checked={showGraphLayer} onChange={(e) => setShowGraphLayer(e.target.checked)} />
                  graph
                </label>
                <label>
                  <input type="checkbox" checked={showRouteLayer} onChange={(e) => setShowRouteLayer(e.target.checked)} />
                  route
                </label>
                <button onClick={() => setStageScale((s) => Math.max(0.5, s - 0.1))}>-</button>
                <span>{Math.round(stageScale * 100)}%</span>
                <button onClick={() => setStageScale((s) => Math.min(2.5, s + 0.1))}>+</button>
              </div>

              <Stage
                width={900}
                height={560}
                scale={{ x: stageScale, y: stageScale }}
                x={stagePosition.x}
                y={stagePosition.y}
                draggable
                onDragEnd={(event) => setStagePosition({ x: event.target.x(), y: event.target.y() })}
                onMouseDown={onStagePointerDown}
              >
                {showSourceLayer && (
                  <Layer>
                    {imageElement && <KonvaImage image={imageElement} width={900} height={560} opacity={0.85} />}
                  </Layer>
                )}

                {/* Основной слой объектов оцифровки/ручного редактирования */}
                {showDigitizedLayer && (
                  <Layer>
                    {objects.map((obj) => {
                      const stroke = OBJECT_COLORS[obj.terrainClass];
                      const isSelected = obj.id === selectedObjectId;
                      if (obj.geometryKind === 'Point') {
                        const [point] = obj.points;
                        return (
                          <Circle
                            key={obj.id}
                            x={point.x}
                            y={point.y}
                            radius={isSelected ? 8 : 6}
                            fill={stroke}
                            stroke={isSelected ? '#FFFFFF' : '#111827'}
                            strokeWidth={2}
                            draggable={isSelected}
                            onClick={() => setSelectedObjectId(obj.id)}
                            onDragEnd={(event) => {
                              updateObject(obj.id, (current) => ({
                                ...current,
                                points: [{ x: event.target.x(), y: event.target.y() }],
                              }));
                            }}
                          />
                        );
                      }

                      const points = obj.points.flatMap((p) => [p.x, p.y]);
                      return (
                        <Line
                          key={obj.id}
                          points={points}
                          closed={obj.geometryKind === 'Polygon'}
                          fill={obj.geometryKind === 'Polygon' ? `${stroke}55` : undefined}
                          stroke={stroke}
                          strokeWidth={isSelected ? 4 : 3}
                          onClick={() => setSelectedObjectId(obj.id)}
                        />
                      );
                    })}

                    {selectedObject &&
                      selectedObject.geometryKind !== 'Point' &&
                      selectedObject.points.map((point, index) => (
                        <Circle
                          key={`${selectedObject.id}-${index}`}
                          x={point.x}
                          y={point.y}
                          radius={6}
                          fill="#FFFFFF"
                          stroke="#111827"
                          strokeWidth={2}
                          draggable
                          onDragEnd={(event) => {
                            updateObject(selectedObject.id, (current) => ({
                              ...current,
                              points: current.points.map((p, i) =>
                                i === index ? { x: event.target.x(), y: event.target.y() } : p,
                              ),
                            }));
                          }}
                        />
                      ))}

                    {draftPoints.length > 0 && <Line points={draftLinePoints} stroke="#F97316" dash={[8, 6]} strokeWidth={2} />}
                  </Layer>
                )}

                {/* Временный плейсхолдер граф-слоя для переключателя Wave 2 */}
                {showGraphLayer && (
                  <Layer>
                    <Line points={[100, 100, 180, 150, 240, 130, 300, 220]} stroke="#A78BFA" strokeWidth={2} dash={[5, 5]} />
                  </Layer>
                )}

                {/* Временный плейсхолдер route-слоя для переключателя Wave 2 */}
                {showRouteLayer && (
                  <Layer>
                    <Line points={[120, 440, 260, 320, 420, 290, 620, 180, 760, 120]} stroke="#F43F5E" strokeWidth={4} />
                  </Layer>
                )}
              </Stage>
            </div>

            <aside className="side-panel">
              <h3>Selected object</h3>
              {!selectedObject && <p>No object selected</p>}
              {selectedObject && (
                <div className="side-group">
                  <label>
                    Class
                    <select
                      value={selectedObject.terrainClass}
                      onChange={(event) =>
                        updateObject(selectedObject.id, (current) => ({
                          ...current,
                          terrainClass: event.target.value as TerrainClass,
                        }))
                      }
                    >
                      {CLASS_OPTIONS.map((item) => (
                        <option key={item} value={item}>
                          {item}
                        </option>
                      ))}
                    </select>
                  </label>
                  <label>
                    Terrain type
                    <select
                      value={selectedObject.terrainObjectTypeId ?? ''}
                      onChange={(event) =>
                        updateObject(selectedObject.id, (current) => ({
                          ...current,
                          terrainObjectTypeId: event.target.value || null,
                        }))
                      }
                    >
                      <option value="">None</option>
                      {terrainTypes.map((type) => (
                        <option key={type.id} value={type.id}>
                          {type.name}
                        </option>
                      ))}
                    </select>
                  </label>
                  <label>
                    Traversability
                    <input
                      type="number"
                      min={0.05}
                      max={10}
                      step={0.05}
                      value={selectedObject.traversability}
                      onChange={(event) =>
                        updateObject(selectedObject.id, (current) => ({
                          ...current,
                          traversability: Number(event.target.value),
                        }))
                      }
                    />
                  </label>
                  <button onClick={onDeleteObject}>Delete object</button>
                </div>
              )}

              <h3>Terrain types</h3>
              <div className="side-group">
                <button onClick={onCreateTerrainType}>Add custom type</button>
                <ul className="types-list">
                  {terrainTypes.map((type) => (
                    <li key={type.id}>
                      <span style={{ color: type.color }}>{type.name}</span>
                      {!type.isSystem && (
                        <span className="types-actions">
                          <button onClick={() => onUpdateTerrainType(type)}>Edit</button>
                          <button onClick={() => onDeleteTerrainType(type.id)}>Delete</button>
                        </span>
                      )}
                    </li>
                  ))}
                </ul>
              </div>

              <h3>Digitization status</h3>
              {!digitizeState && <p>Not started</p>}
              {digitizeState && (
                <div className="side-group">
                  <p>Status: {digitizeState.status}</p>
                  <p>Progress: {digitizeState.progress}%</p>
                  <p>Macro F1: {digitizeState.macroF1 ?? '-'}</p>
                  <p>IoU: {digitizeState.ioU ?? '-'}</p>
                  {digitizeState.error && <p className="error">{digitizeState.error}</p>}
                </div>
              )}
            </aside>
          </div>
        </section>
      )}

      {mapDetails && <p className="hint">Active map: {mapDetails.name}</p>}
      {error && <p className="error">{error}</p>}
    </div>
  );
}
