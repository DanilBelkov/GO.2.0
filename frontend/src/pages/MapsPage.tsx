import { useEffect, useState } from 'react';
import type { ChangeEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { getMaps, uploadMap } from '../lib/api';
import type { MapListItem } from '../lib/api';
import { clearAuth } from '../lib/auth';

export function MapsPage() {
  const navigate = useNavigate();
  const [maps, setMaps] = useState<MapListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [uploading, setUploading] = useState(false);

  async function loadMaps() {
    setLoading(true);
    setError('');
    try {
      const data = await getMaps();
      setMaps(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load maps');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void loadMaps();
  }, []);

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

  function onLogout() {
    clearAuth();
    navigate('/login');
  }

  return (
    <div className="page maps">
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
                <tr key={map.id}>
                  <td>{map.name}</td>
                  <td>{map.status}</td>
                  <td>{new Date(map.createdAtUtc).toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {error && <p className="error">{error}</p>}
      </section>
    </div>
  );
}
