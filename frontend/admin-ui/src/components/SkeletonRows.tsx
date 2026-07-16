// Skeleton loading placeholder: pulsing bars where content is about to
// appear. Friendlier than "Loading..." text because the page keeps its
// shape while data arrives.
export function SkeletonRows({ rows = 4 }: { rows?: number }) {
  return (
    <div aria-hidden="true" style={{ display: 'flex', flexDirection: 'column', gap: '0.55rem', padding: '0.3rem 0' }}>
      {Array.from({ length: rows }, (_, i) => (
        <span key={i} className="skeleton" style={{ width: `${88 - (i % 3) * 14}%`, height: '0.95rem' }} />
      ))}
    </div>
  )
}
