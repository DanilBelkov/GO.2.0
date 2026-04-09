const ACCESS_TOKEN_KEY = 'go2.accessToken';
const REFRESH_TOKEN_KEY = 'go2.refreshToken';

// Читает access token из localStorage.
export function getAccessToken(): string | null {
  return localStorage.getItem(ACCESS_TOKEN_KEY);
}

type JwtPayload = {
  role?: string;
  ['http://schemas.microsoft.com/ws/2008/06/identity/claims/role']?: string;
};

function decodeBase64Url(input: string): string {
  const normalized = input.replace(/-/g, '+').replace(/_/g, '/');
  const padLength = normalized.length % 4 === 0 ? 0 : 4 - (normalized.length % 4);
  return atob(`${normalized}${'='.repeat(padLength)}`);
}

export function getCurrentUserRole(): string | null {
  const token = getAccessToken();
  if (!token) {
    return null;
  }

  const parts = token.split('.');
  if (parts.length < 2) {
    return null;
  }

  try {
    const payloadJson = decodeBase64Url(parts[1]);
    const payload = JSON.parse(payloadJson) as JwtPayload;
    return payload.role ?? payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] ?? null;
  } catch {
    return null;
  }
}

// Читает refresh token из localStorage.
export function getRefreshToken(): string | null {
  return localStorage.getItem(REFRESH_TOKEN_KEY);
}

// Сохраняет пару токенов после login/register/refresh.
export function saveAuth(accessToken: string, refreshToken: string): void {
  localStorage.setItem(ACCESS_TOKEN_KEY, accessToken);
  localStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
}

// Полный выход из сессии на клиенте.
export function clearAuth(): void {
  localStorage.removeItem(ACCESS_TOKEN_KEY);
  localStorage.removeItem(REFRESH_TOKEN_KEY);
}

