import { clearAuth, getAccessToken, getRefreshToken, saveAuth } from './auth';

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5000';

async function request<T>(url: string, init: RequestInit = {}, retry = true): Promise<T> {
  const headers = new Headers(init.headers ?? {});
  const token = getAccessToken();
  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  }

  const response = await fetch(`${API_BASE}${url}`, { ...init, headers });
  if (response.status === 401 && retry) {
    const refreshed = await tryRefresh();
    if (refreshed) {
      return request<T>(url, init, false);
    }

    clearAuth();
  }

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `Request failed: ${response.status}`);
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
  });

  if (!response.ok) {
    return false;
  }

  const data = (await response.json()) as { accessToken: string; refreshToken: string };
  saveAuth(data.accessToken, data.refreshToken);
  return true;
}

export type AuthResponse = {
  accessToken: string;
  refreshToken: string;
  expiresAtUtc: string;
};

export type MapListItem = {
  id: string;
  name: string;
  status: 'Uploaded' | 'Digitized' | 'Edited' | 'Ready';
  createdAtUtc: string;
};

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

export async function getMaps(): Promise<MapListItem[]> {
  return request<MapListItem[]>('/maps');
}

export async function uploadMap(file: File): Promise<void> {
  const body = new FormData();
  body.append('file', file);
  await request('/maps/upload', { method: 'POST', body });
}

