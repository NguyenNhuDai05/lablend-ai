import { useCallback, useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { useAuth } from '../auth/AuthContext';
import { errorText, singleFlight } from '../lib/workflows';
import { Icon } from './Icon';

export function useResource<T>(key: string, load: (signal: AbortSignal) => Promise<T>, pollMs = 0) {
  const { user, status } = useAuth();
  const loader = useRef(load); loader.current = load;
  const [version, setVersion] = useState(0);
  const [state, setState] = useState<{ data?: T; loading: boolean; error?: string }>({ loading: true });
  useEffect(() => {
    let alive = true; let current: AbortController | undefined;
    async function fetchData(initial: boolean) {
      current?.abort(); current = new AbortController();
      const controller = current;
      if (initial) setState({ loading: true });
      try { const data = await loader.current(controller.signal); if (alive && !controller.signal.aborted) setState({ data, loading: false }); }
      catch (e) { if (alive && !controller.signal.aborted) setState(s => ({ ...s, loading: false, error: errorText(e) })); }
    }
    void fetchData(true);
    const timer = pollMs ? setInterval(() => { if (document.visibilityState === 'visible') void fetchData(false); }, pollMs) : undefined;
    return () => { alive = false; current?.abort(); if (timer) clearInterval(timer); };
  }, [key, user?.id, status, version, pollMs]);
  return { ...state, reload: useCallback(() => setVersion(v => v + 1), []) };
}
export function useCommand() {
  const flight = useRef(singleFlight());
  const [busy, setBusy] = useState(false); const [error, setError] = useState('');
  const alive = useRef(true);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  const run = (work: () => Promise<void>) => flight.current(async () => {
    setBusy(true); setError('');
    try { await work(); }
    catch (e) { if (alive.current) setError(errorText(e)); }
    finally { if (alive.current) setBusy(false); }
  });
  return { busy, error, run, clearError: () => setError('') };
}
/** Shows the first 8 characters of a UUID; the full value stays in the tooltip and the copy button. */
export function ShortId({ id }: { id: string }) {
  const [copied, setCopied] = useState(false);
  const short = id.slice(0, 8);
  return <span className="short-id" title={id}><code>{short}</code><button type="button" className="copy-id" aria-label={copied ? `Đã sao chép mã ${short}` : `Sao chép mã ${short}`}
    onClick={() => void navigator.clipboard?.writeText(id).then(() => setCopied(true), () => undefined)}><Icon name={copied ? 'check' : 'copy'} size={14} /></button></span>;
}
export function InlineError({ message }: { message?: string }) { return message ? <p className="form-error" role="alert">{message}</p> : null; }
export function ReviewDialog({ title, children, busy, onClose, onConfirm, confirmLabel }: { title: string; children: ReactNode; busy: boolean; onClose: () => void; onConfirm: () => void; confirmLabel: string }) {
  const dialog = useRef<HTMLDialogElement>(null); const titleId = useId();
  useEffect(() => {
    const previous = document.activeElement; const element = dialog.current;
    element?.showModal();
    return () => { element?.close(); if (previous instanceof HTMLElement && previous.isConnected) previous.focus(); };
  }, []);
  return <dialog ref={dialog} className="review-dialog" aria-labelledby={titleId} onCancel={e => { e.preventDefault(); if (!busy) onClose(); }}>
    <h2 id={titleId}>{title}</h2>{children}<div className="actions"><button type="button" autoFocus className="button-quiet" disabled={busy} onClick={onClose}>Quay lại chỉnh sửa</button><button type="button" disabled={busy} onClick={onConfirm}>{busy ? 'Đang xử lý…' : confirmLabel}</button></div>
  </dialog>;
}
export function Pager({ page, total, pageSize, onChange }: { page: number; total: number; pageSize: number; onChange: (p: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  return <nav className="pagination" aria-label="Phân trang"><button disabled={page <= 1} onClick={() => onChange(page - 1)}>Trước</button><span>Trang {page}/{pages} · {total} mục</span><button disabled={page >= pages} onClick={() => onChange(page + 1)}>Sau</button></nav>;
}
