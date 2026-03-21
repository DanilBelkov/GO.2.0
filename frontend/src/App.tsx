import type { ReactElement } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { LoginPage } from './pages/LoginPage';
import { MapsPage } from './pages/MapsPage';
import { RegisterPage } from './pages/RegisterPage';
import { getAccessToken } from './lib/auth';

// Обертка маршрутов, требующих авторизации.
function ProtectedRoute({ children }: { children: ReactElement }) {
  const token = getAccessToken();
  if (!token) {
    return <Navigate to="/login" replace />;
  }

  return children;
}

export default function App() {
  // Корневая схема маршрутов приложения.
  return (
    <Routes>
      <Route path="/" element={<Navigate to="/maps" replace />} />
      <Route path="/login" element={<LoginPage />} />
      <Route path="/register" element={<RegisterPage />} />
      <Route
        path="/maps"
        element={
          <ProtectedRoute>
            <MapsPage />
          </ProtectedRoute>
        }
      />
    </Routes>
  );
}
