import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api'

// Peer benchmarking — how this tenant compares to the anonymized platform
// peer set. Each peer contributes one number; nothing is shown below the
// minimum peer count, and the tenant is excluded from its own peer group.

interface PeerBenchmarkResult {
  scoreType: string; available: boolean; reason?: string | null
  yourAverage?: number | null; yourLatest?: number | null
  peerCount: number; peerMedian?: number | null; peerP25?: number | null; peerP75?: number | null
}

const SCORE_TYPES = [
  { id: 'KPIActual', label: 'KPI performance (overall)' },
  { id: 'KPIVariance', label: 'KPI variance vs target' },
  { id: 'CompositeHealth', label: 'Composite health' },
]

export function PeerBenchmark({ tenant, user }: { tenant: string; user: string }) {
  const [scoreType, setScoreType] = useState('KPIActual')
  const [data, setData] = useState<PeerBenchmarkResult | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async (st: string) => {
    setError(null); setData(null)
    try { setData(await api.get<PeerBenchmarkResult>(`/api/analytics/peer-benchmark?scoreType=${encodeURIComponent(st)}`)) }
    catch (e) { setError(e instanceof ApiError ? e.message : String(e)) }
  }, [])
  useEffect(() => { void load(scoreType) }, [load, scoreType, tenant, user])

  // Scale for the bar: cover your value and the peer spread.
  const vals = data ? [data.yourAverage, data.peerMedian, data.peerP25, data.peerP75].filter((v): v is number => typeof v === 'number') : []
  const max = Math.max(1, ...vals) * 1.1
  const pct = (v?: number | null) => typeof v === 'number' ? `${Math.max(0, Math.min(100, (v / max) * 100))}%` : '0%'

  return (
    <div>
      <section className="panel">
        <h2>Peer benchmark</h2>
        <p className="muted">
          How <strong>{tenant}</strong> compares to the anonymized platform peer set. Each peer
          contributes a single number; nothing is shown unless there are enough peers to keep it
          anonymous, and this tenant is excluded from its own comparison.
        </p>
        <label>Metric
          <select value={scoreType} onChange={(e) => setScoreType(e.target.value)}>
            {SCORE_TYPES.map((s) => <option key={s.id} value={s.id}>{s.label}</option>)}
          </select>
        </label>
        {error && <p className="error small">{error}</p>}
      </section>

      <section className="panel">
        {data === null ? <p className="muted">Loading…</p>
          : !data.available ? <p className="muted">{data.reason ?? 'Not enough peers to benchmark this metric yet.'}</p>
          : (
            <>
              <p className="small">
                Across <strong>{data.peerCount}</strong> peers · your average{' '}
                <strong>{data.yourAverage?.toLocaleString() ?? '—'}</strong>
                {typeof data.yourAverage === 'number' && typeof data.peerMedian === 'number' && (
                  <span className={data.yourAverage >= data.peerMedian ? 'error-text' : ''} style={{ marginLeft: '0.4rem' }}>
                    ({data.yourAverage >= data.peerMedian ? 'above' : 'below'} peer median)
                  </span>
                )}
              </p>
              <div className="peer-bars">
                <div className="peer-row"><span className="peer-label">You (avg)</span><div className="peer-track"><div className="peer-fill you" style={{ width: pct(data.yourAverage) }} /></div><span className="peer-val">{data.yourAverage?.toLocaleString() ?? '—'}</span></div>
                <div className="peer-row"><span className="peer-label">Peer P25</span><div className="peer-track"><div className="peer-fill" style={{ width: pct(data.peerP25) }} /></div><span className="peer-val">{data.peerP25?.toLocaleString() ?? '—'}</span></div>
                <div className="peer-row"><span className="peer-label">Peer median</span><div className="peer-track"><div className="peer-fill" style={{ width: pct(data.peerMedian) }} /></div><span className="peer-val">{data.peerMedian?.toLocaleString() ?? '—'}</span></div>
                <div className="peer-row"><span className="peer-label">Peer P75</span><div className="peer-track"><div className="peer-fill" style={{ width: pct(data.peerP75) }} /></div><span className="peer-val">{data.peerP75?.toLocaleString() ?? '—'}</span></div>
              </div>
              <p className="muted small">Your latest reading: {data.yourLatest?.toLocaleString() ?? '—'}.</p>
            </>
          )}
      </section>
    </div>
  )
}
