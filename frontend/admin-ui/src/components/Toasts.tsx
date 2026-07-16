import { createContext, useCallback, useContext, useRef, useState, type ReactNode } from 'react'

// Minimal toast system: success/error notifications stacked top-right,
// auto-dismissing, screen-reader announced. Mutation screens use this for
// action feedback so results are visible wherever the user is scrolled;
// inline field-level errors stay where they are.

export interface Toast {
  id: number
  kind: 'success' | 'error'
  message: string
}

interface ToastApi {
  success: (message: string) => void
  error: (message: string) => void
}

const ToastContext = createContext<ToastApi>({ success: () => {}, error: () => {} })

export function useToast(): ToastApi {
  return useContext(ToastContext)
}

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([])
  const nextId = useRef(1)

  const push = useCallback((kind: Toast['kind'], message: string) => {
    const id = nextId.current++
    setToasts((prev) => [...prev.slice(-4), { id, kind, message }])
    window.setTimeout(() => setToasts((prev) => prev.filter((t) => t.id !== id)), 4000)
  }, [])

  const api = useRef<ToastApi>({
    success: (m) => push('success', m),
    error: (m) => push('error', m),
  })
  // Keep the stable ref wired to the latest push (push itself is stable).
  api.current.success = (m) => push('success', m)
  api.current.error = (m) => push('error', m)

  return (
    <ToastContext.Provider value={api.current}>
      {children}
      <div className="toasts" aria-live="polite" aria-atomic="false">
        {toasts.map((t) => (
          <div key={t.id} className={`toast toast-${t.kind}`} role="status">
            <span aria-hidden>{t.kind === 'success' ? '✓' : '⚠'}</span>
            <span className="toast-msg">{t.message}</span>
            <button
              className="toast-close"
              aria-label="Dismiss notification"
              onClick={() => setToasts((prev) => prev.filter((x) => x.id !== t.id))}
            >×</button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  )
}
