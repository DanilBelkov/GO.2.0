const ACCESS_TOKEN_KEY = 'go2.accessToken';
const REFRESH_TOKEN_KEY = 'go2.refreshToken';

// Читает access token из localStorage.
export function getAccessToken(): string | null {
  return localStorage.getItem(ACCESS_TOKEN_KEY);
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

