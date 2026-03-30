import { clearAuth, getAccessToken, getRefreshToken, saveAuth } from './auth';

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5299';

// Базовый HTTP-клиент: добавляет access token, обрабатывает refresh и ошибки API.
async function request<T>(url: string, init: RequestInit = {}, retry = true): Promise<T> {
  const headers = new Headers(init.headers ?? {});
  const token = getAccessToken();
  const method = (init.method ?? 'GET').toUpperCase();
  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  }

  if (method === 'GET') {
    headers.set('Cache-Control', 'no-cache, no-store, max-age=0');
    headers.set('Pragma', 'no-cache');
  }

  const response = await fetch(`${API_BASE}${url}`, {
    ...init,
    headers,
    cache: method === 'GET' ? 'no-store' : init.cache,
  });
  if (response.status === 401 && retry) {
    const refreshed = await tryRefresh();
    if (refreshed) {
      return request<T>(url, init, false);
    }

    clearAuth();
  }

  if (!response.ok) {
    const rawText = await response.text();
    let parsedMessage = '';
    try {
      const parsed = JSON.parse(rawText) as { title?: string; detail?: string };
      parsedMessage = parsed.detail || parsed.title || '';
    } catch {
      parsedMessage = '';
    }

    throw new Error(parsedMessage || rawText || `Request failed: ${response.status}`);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json() as Promise<T>;
}

async function tryRefresh(): Promise<boolean> {
  const refreshToken = getRefreshToken();
  if (!refreshToken) {
    return false;
  }

  const response = await fetch(`${API_BASE}/auth/refresh`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken }),
    cache: 'no-store',
  });

  if (!response.ok) {
    return false;
  }

  const data = (await response.json()) as { accessToken: string; refreshToken: string };
  saveAuth(data.accessToken, data.refreshToken);
  return true;
}

// Контракты auth.
export type AuthResponse = {
  accessToken: string;
  refreshToken: string;
  expiresAtUtc: string;
};

// Контракты карт.
export type MapStatus = 'Uploaded' | 'Digitized' | 'Edited' | 'Ready';

export type MapListItem = {
  id: string;
  name: string;
  status: MapStatus;
  createdAtUtc: string;
};

export type MapDetails = MapListItem & {
  activeVersionId: string | null;
};

export type MapVersion = {
  id: string;
  versionNumber: number;
  createdAtUtc: string;
  notes: string;
};

// Контракты домена оцифровки/редактора.
export type TerrainClass = 'Vegetation' | 'Water' | 'Rock' | 'Ground' | 'ManMade';
export type TerrainGeometryKind = 'Point' | 'Line' | 'Polygon';
export type TerrainObjectSource = 'Auto' | 'Manual';

export type TerrainType = {
  id: string;
  name: string;
  color: string;
  icon: string;
  traversability: number;
  comment: string;
  isSystem: boolean;
};

export type TerrainObject = {
  id: string;
  terrainClass: TerrainClass;
  terrainObjectTypeId: string | null;
  geometryKind: TerrainGeometryKind;
  geometryJson: string;
  traversability: number;
  source: TerrainObjectSource;
};

export type TerrainObjectInput = {
  id?: string;
  terrainClass: TerrainClass;
  terrainObjectTypeId: string | null;
  geometryKind: TerrainGeometryKind;
  geometryJson: string;
  traversability: number;
};

export type DigitizationJob = {
  jobId: string;
  status: 'Queued' | 'Running' | 'Completed' | 'Failed';
  progress: number;
  error: string;
  macroF1: number | null;
  ioU: number | null;
  mapVersionId: string;
  startedAtUtc: string | null;
  finishedAtUtc: string | null;
};

// Контракты маршрутизации Wave 3.
export type RoutePoint = {
  x: number;
  y: number;
};

export type RouteProfile = {
  timeWeight: number;
  safetyWeight: number;
};

export type RouteSegment = {
  from: RoutePoint;
  to: RoutePoint;
  segmentCost: number;
  segmentRisk: number;
};

export type RouteVariant = {
  rank: number;
  totalCost: number;
  length: number;
  estimatedTime: number;
  riskScore: number;
  penaltyScore: number;
  polyline: RoutePoint[];
  segments: RouteSegment[];
  whyChosen: string[];
};

export type RouteResult = {
  routes: RouteVariant[];
  summary: string;
};

export type RouteJobStatus = {
  jobId: string;
  status: 'in-progress' | 'completed' | 'failed';
  progress: number;
  error: string;
  result: RouteResult | null;
};

export type RouteGraphNode = {
  id: string;
  x: number;
  y: number;
};

export type RouteGraphEdge = {
  fromNodeId: string;
  toNodeId: string;
  weight: number;
};

export type RouteGraph = {
  nodes: RouteGraphNode[];
  edges: RouteGraphEdge[];
  gridWidth: number;
  gridHeight: number;
  summary: string;
};

// Auth endpoints.
export async function register(email: string, password: string): Promise<AuthResponse> {
  return request<AuthResponse>('/auth/register', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
  });
}

export async function login(email: string, password: string): Promise<AuthResponse> {
  return request<AuthResponse>('/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
  });
}

// Maps endpoints.
export async function getMaps(): Promise<MapListItem[]> {
  return request<MapListItem[]>('/maps');
}

export async function getMap(mapId: string): Promise<MapDetails> {
  return request<MapDetails>(`/maps/${mapId}`);
}

export async function getMapVersions(mapId: string): Promise<MapVersion[]> {
  return request<MapVersion[]>(`/maps/${mapId}/versions`);
}

export async function uploadMap(file: File): Promise<void> {
  const body = new FormData();
  body.append('file', file);
  await request('/maps/upload', { method: 'POST', body });
}

export async function uploadOcdMap(file: File): Promise<void> {
  const body = new FormData();
  body.append('file', file);
  await request('/maps/upload-ocd', { method: 'POST', body });
}

// Загружает защищенное изображение карты и возвращает blob URL для <img>/<KonvaImage>.
export async function getMapImageObjectUrl(mapId: string): Promise<string> {
  const token = getAccessToken();
  const headers = new Headers();
  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  }

  const response = await fetch(`${API_BASE}/maps/${mapId}/image?ts=${Date.now()}`, {
    headers,
    cache: 'no-store',
  });
  if (!response.ok) {
    throw new Error('Не удалось загрузить изображение карты');
  }

  const blob = await response.blob();
  return URL.createObjectURL(blob);
}

// Digitization endpoints.
export async function startDigitization(mapId: string, versionId?: string): Promise<{ jobId: string; status: string }> {
  return request(`/maps/${mapId}/digitize`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ versionId }),
  });
}

export async function getDigitizationStatus(mapId: string, jobId: string): Promise<DigitizationJob> {
  return request<DigitizationJob>(`/maps/${mapId}/digitize/${jobId}`);
}

// Terrain objects endpoints.
export async function getTerrainObjects(mapId: string, versionId?: string): Promise<TerrainObject[]> {
  const query = versionId ? `?versionId=${encodeURIComponent(versionId)}` : '';
  return request<TerrainObject[]>(`/maps/${mapId}/objects${query}`);
}

// Сохраняет текущее состояние редактора как новую версию карты.
export async function saveTerrainObjects(
  mapId: string,
  objects: TerrainObjectInput[],
  baseVersionId: string | null,
  notes: string,
): Promise<MapVersion> {
  return request<MapVersion>(`/maps/${mapId}/objects`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      baseVersionId: baseVersionId ?? undefined,
      notes,
      objects,
    }),
  });
}

// Terrain types endpoints.
export async function getTerrainTypes(): Promise<TerrainType[]> {
  return request<TerrainType[]>('/terrain-types');
}

export async function createTerrainType(payload: Omit<TerrainType, 'id' | 'isSystem'>): Promise<TerrainType> {
  return request<TerrainType>('/terrain-types', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });
}

export async function updateTerrainType(
  id: string,
  payload: Omit<TerrainType, 'id' | 'isSystem'>,
): Promise<TerrainType> {
  return request<TerrainType>(`/terrain-types/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });
}

export async function deleteTerrainType(id: string): Promise<void> {
  await request<void>(`/terrain-types/${id}`, { method: 'DELETE' });
}

// Route endpoints.
export async function calculateRoutes(
  mapId: string,
  waypoints: RoutePoint[],
  profile: RouteProfile,
  mapVersionId?: string | null,
): Promise<{ jobId: string; status: string }> {
  return request(`/routes/calculate/${mapId}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      mapVersionId: mapVersionId ?? undefined,
      waypoints,
      profile,
    }),
  });
}

export async function getRouteJobStatus(jobId: string): Promise<RouteJobStatus> {
  return request<RouteJobStatus>(`/routes/${jobId}/status`);
}

export async function getRouteGraph(
  mapId: string,
  mapVersionId?: string | null,
  profile: RouteProfile = { timeWeight: 0.6, safetyWeight: 0.4 },
): Promise<RouteGraph> {
  const query = new URLSearchParams();
  if (mapVersionId) {
    query.set('mapVersionId', mapVersionId);
  }

  query.set('timeWeight', String(profile.timeWeight));
  query.set('safetyWeight', String(profile.safetyWeight));
  query.set('ts', String(Date.now()));
  return request<RouteGraph>(`/routes/graph/${mapId}?${query.toString()}`);
}
