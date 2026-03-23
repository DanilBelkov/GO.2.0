import { useState } from 'react';
import type { FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { register } from '../lib/api';
import { saveAuth } from '../lib/auth';

// Страница регистрации нового пользователя.
export function RegisterPage() {
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    // После регистрации сразу логиним пользователя через выданные токены.
    setLoading(true);
    setError('');
    try {
      const response = await register(email, password);
      saveAuth(response.accessToken, response.refreshToken);
      navigate('/maps');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Ошибка регистрации');
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="page auth">
      <form className="panel" onSubmit={onSubmit}>
        <h1>Регистрация</h1>
        <label>
          Почта
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
        </label>
        <label>
          Пароль
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} required minLength={8} />
        </label>
        {error && <p className="error">{error}</p>}
        <button type="submit" disabled={loading}>
          {loading ? 'Создаем...' : 'Создать аккаунт'}
        </button>
        <p className="hint">
          Уже есть аккаунт? <Link to="/login">Войти</Link>
        </p>
      </form>
    </div>
  );
}
