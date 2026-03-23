import { useState } from 'react';
import type { FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { login } from '../lib/api';
import { saveAuth } from '../lib/auth';

// Страница входа пользователя.
export function LoginPage() {
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    // Отправляем credentials и сохраняем токены локально.
    setLoading(true);
    setError('');
    try {
      const response = await login(email, password);
      saveAuth(response.accessToken, response.refreshToken);
      navigate('/maps');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Ошибка входа');
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="page auth">
      <form className="panel" onSubmit={onSubmit}>
        <h1>GO2 Вход</h1>
        <label>
          Почта
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
        </label>
        <label>
          Пароль
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />
        </label>
        {error && <p className="error">{error}</p>}
        <button type="submit" disabled={loading}>
          {loading ? 'Входим...' : 'Войти'}
        </button>
        <p className="hint">
          Нет аккаунта? <Link to="/register">Создать</Link>
        </p>
      </form>
    </div>
  );
}
