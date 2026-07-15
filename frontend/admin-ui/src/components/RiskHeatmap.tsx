import { useState } from 'react'

// Risk Heatmap (ECARMF-ADR-UIUX-001 Phase 1, §2.3). A single reusable
// severity × likelihood matrix. Severity is encoded THREE ways so it never
// depends on colour alone (accessibility): the cell's colour band, its row
// position, and a per-band glyph. Likelihood is the column. Clicking a cell
// lists the risks that fall in it.

export interface RiskPoint {
  id: string
  label: string
  /** 1..5 */
  severity: number
  /** 1..5 */
  likelihood: number
  group?: string
  score?: number
}

const SEV_LABEL = ['', 'Low', 'Guarded', 'Elevated', 'High', 'Critical']
const SEV_GLYPH = ['', '·', '▪', '▲', '◆', '⬢'] // shape, not colour, carries severity

// Risk zone by severity×likelihood product — the standard 5×5 banding.
function zone(sev: number, like: number): { cls: string; name: string } {
  const p = sev * like
  if (p >= 15) return { cls: 'z-critical', name: 'Critical' }
  if (p >= 9) return { cls: 'z-high', name: 'High' }
  if (p >= 4) return { cls: 'z-medium', name: 'Medium' }
  return { cls: 'z-low', name: 'Low' }
}

const clamp = (n: number) => Math.max(1, Math.min(5, Math.round(n)))

export function RiskHeatmap({ risks }: { risks: RiskPoint[] }) {
  const [sel, setSel] = useState<{ s: number; l: number } | null>(null)

  // Bucket risks into cells keyed "sev,like".
  const cells = new Map<string, RiskPoint[]>()
  for (const r of risks) {
    const key = `${clamp(r.severity)},${clamp(r.likelihood)}`
    ;(cells.get(key) ?? cells.set(key, []).get(key)!).push(r)
  }

  const selected = sel ? (cells.get(`${sel.s},${sel.l}`) ?? []) : []

  return (
    <div className="heatmap-wrap">
      <div className="heatmap" role="grid" aria-label="Risk heatmap: severity by likelihood">
        <div className="hm-corner" aria-hidden="true">S \ L</div>
        {[1, 2, 3, 4, 5].map((l) => (
          <div key={`col-${l}`} className="hm-col-head" role="columnheader">{l}</div>
        ))}

        {[5, 4, 3, 2, 1].map((s) => (
          <div key={`row-${s}`} style={{ display: 'contents' }}>
            <div className="hm-row-head" role="rowheader" title={SEV_LABEL[s]}>
              <span className="hm-glyph">{SEV_GLYPH[s]}</span> {s}
            </div>
            {[1, 2, 3, 4, 5].map((l) => {
              const key = `${s},${l}`
              const items = cells.get(key) ?? []
              const z = zone(s, l)
              const active = sel?.s === s && sel?.l === l
              return (
                <button
                  key={key}
                  role="gridcell"
                  className={`hm-cell ${z.cls} ${active ? 'active' : ''} ${items.length ? 'has-risks' : ''}`}
                  aria-label={`Severity ${s} (${SEV_LABEL[s]}), likelihood ${l}, ${z.name} zone, ${items.length} risk(s)`}
                  onClick={() => setSel(active ? null : { s, l })}
                  disabled={items.length === 0}
                >
                  {items.length > 0 && (
                    <>
                      <span className="hm-glyph">{SEV_GLYPH[s]}</span>
                      <span className="hm-count">{items.length}</span>
                    </>
                  )}
                </button>
              )
            })}
          </div>
        ))}
      </div>

      <div className="hm-legend" aria-hidden="true">
        <span className="hm-leg z-low">Low</span>
        <span className="hm-leg z-medium">Medium</span>
        <span className="hm-leg z-high">High</span>
        <span className="hm-leg z-critical">Critical</span>
        <span className="muted small">— shape marks severity band, position marks the cell (not colour alone)</span>
      </div>

      {sel && (
        <div className="hm-detail panel">
          <h3>
            Severity {sel.s} ({SEV_LABEL[sel.s]}) · Likelihood {sel.l} — {zone(sel.s, sel.l).name} zone
            <button className="secondary" style={{ marginLeft: '0.6rem' }} onClick={() => setSel(null)}>Close</button>
          </h3>
          {selected.length === 0 ? (
            <p className="muted small">No risks in this cell.</p>
          ) : (
            <ul className="hm-risk-list">
              {selected.map((r) => (
                <li key={r.id}>
                  <span className="mono small">{r.id}</span> {r.label}
                  {r.group && <span className="muted small"> · {r.group}</span>}
                  {r.score != null && <span className="muted small"> · score {r.score}</span>}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  )
}
