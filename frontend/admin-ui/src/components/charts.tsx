// Design System 2.0 chart kit — tiny hand-rolled SVG visuals (no library).
// Everything draws with design tokens via currentColor/CSS variables so the
// charts follow the theme automatically. These are deliberately simple:
// at-a-glance shapes next to the real numbers, never a replacement for them.

const PALETTE = [
  'var(--accent)', 'var(--ok-text)', 'var(--warn-text)', 'var(--bad-text)',
  'var(--info-text)', 'var(--accent-2)', 'var(--text-3)',
]

/** Compact line-with-area trend, e.g. records per period. */
export function Sparkline({ values, width = 120, height = 34, stroke = 'var(--accent)' }: {
  values: number[]; width?: number; height?: number; stroke?: string
}) {
  if (values.length < 2) return null
  const max = Math.max(...values, 1)
  const min = Math.min(...values, 0)
  const span = max - min || 1
  const pad = 3
  const pts = values.map((v, i) => [
    pad + (i * (width - pad * 2)) / (values.length - 1),
    height - pad - ((v - min) / span) * (height - pad * 2),
  ])
  const line = pts.map(([x, y]) => `${x.toFixed(1)},${y.toFixed(1)}`).join(' ')
  const area = `${pad},${height - pad} ${line} ${(width - pad).toFixed(1)},${height - pad}`
  const last = pts[pts.length - 1]
  return (
    <svg width={width} height={height} viewBox={`0 0 ${width} ${height}`} aria-hidden="true" style={{ display: 'block' }}>
      <polygon points={area} fill={stroke} opacity="0.12" />
      <polyline points={line} fill="none" stroke={stroke} strokeWidth="1.8" strokeLinejoin="round" strokeLinecap="round" />
      <circle cx={last[0]} cy={last[1]} r="2.4" fill={stroke} />
    </svg>
  )
}

/** Donut breakdown with center total; pair with a legend of the same data. */
export function Donut({ data, size = 110, thickness = 13 }: {
  data: { label: string; value: number; color?: string }[]; size?: number; thickness?: number
}) {
  const total = data.reduce((s, d) => s + d.value, 0)
  if (total <= 0) return null
  const r = (size - thickness) / 2
  const circ = 2 * Math.PI * r
  let offset = 0
  return (
    <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} role="img" aria-label={data.map((d) => `${d.label} ${d.value}`).join(', ')}>
      <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke="var(--bg-hover)" strokeWidth={thickness} />
      {data.map((d, i) => {
        const frac = d.value / total
        const seg = (
          <circle
            key={d.label}
            cx={size / 2} cy={size / 2} r={r} fill="none"
            stroke={d.color ?? PALETTE[i % PALETTE.length]}
            strokeWidth={thickness}
            strokeDasharray={`${(frac * circ).toFixed(2)} ${circ.toFixed(2)}`}
            strokeDashoffset={(-offset * circ).toFixed(2)}
            transform={`rotate(-90 ${size / 2} ${size / 2})`}
          />
        )
        offset += frac
        return seg
      })}
      <text x="50%" y="50%" textAnchor="middle" dominantBaseline="central"
        fill="var(--text-1)" fontSize={size / 5.2} fontWeight="600">{total.toLocaleString()}</text>
    </svg>
  )
}

/** Legend rows for a Donut (same data, same color assignment). */
export function DonutLegend({ data }: { data: { label: string; value: number; color?: string }[] }) {
  const total = data.reduce((s, d) => s + d.value, 0) || 1
  return (
    <div className="chart-legend">
      {data.map((d, i) => (
        <div key={d.label} className="chart-legend-row">
          <span className="chart-swatch" style={{ background: d.color ?? PALETTE[i % PALETTE.length] }} />
          <span className="chart-legend-label">{d.label}</span>
          <span className="chart-legend-value">{d.value.toLocaleString()} <span className="muted">({Math.round((d.value / total) * 100)}%)</span></span>
        </div>
      ))}
    </div>
  )
}

/** Inline proportional bar for table rows (value relative to max). */
export function MeterBar({ value, max, color = 'var(--accent)' }: { value: number; max: number; color?: string }) {
  const pct = max > 0 ? Math.max(0, Math.min(100, (value / max) * 100)) : 0
  return (
    <span className="meter" aria-hidden="true">
      <span className="meter-fill" style={{ width: `${pct}%`, background: color }} />
    </span>
  )
}

export const chartPalette = PALETTE
