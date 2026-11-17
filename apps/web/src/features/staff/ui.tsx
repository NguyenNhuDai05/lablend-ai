import { useEffect, useId, useRef, useState, type FormEvent, type ReactNode } from 'react';
import type { AssetStatus } from './api';

// ponytail: Staff-local building blocks; move to M3's shared components once the shell has them.
const errorText = (e: unknown) => e instanceof Error ? e.message : 'Lỗi không xác định.';

export function Modal({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  const ref = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  useEffect(() => {
    const previous = document.activeElement; const dialog = ref.current;
    dialog?.showModal();
    // Do not close here: React StrictMode replays effects and native close would dismiss the parent dialog.
    return () => { if (previous instanceof HTMLElement && previous.isConnected) previous.focus(); };
  }, []);
  return <dialog ref={ref} className="modal" aria-labelledby={titleId} onClose={onClose}>
    <h2 id={titleId}>{title}</h2>
    {children}
  </dialog>;
}

/** Native form in a modal: closes on success, keeps the error inside the dialog otherwise. */
export function FormModal({ title, submitLabel, onSubmit, onClose, children }: {
  title: string; submitLabel: string; onSubmit: (form: FormData) => Promise<unknown>; onClose: () => void; children: ReactNode;
}) {
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const submitting = useRef(false);
  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (submitting.current) return;
    submitting.current = true;
    setBusy(true);
    setError(undefined);
    try {
      await onSubmit(new FormData(e.currentTarget));
      onClose();
    } catch (err) {
      setError(errorText(err));
      setBusy(false);
      submitting.current = false;
    }
  }
  return <Modal title={title} onClose={onClose}>
    <form className="form" onSubmit={e => void submit(e)}>
      {children}
      {error && <p role="alert" className="error">{error}</p>}
      <div className="actions">
        <button type="button" className="ghost" onClick={onClose}>Hủy</button>
        <button disabled={busy}>{busy ? 'Đang lưu…' : submitLabel}</button>
      </div>
    </form>
  </Modal>;
}

export function Field({ label, children }: { label: string; children: ReactNode }) {
  return <label className="field"><span>{label}</span>{children}</label>;
}

/** Runs a mutation, then shows the outcome and reloads the list. */
export function useAction(reload: () => void) {
  const [notice, setNotice] = useState<{ ok: boolean; text: string }>();
  async function act(run: () => Promise<unknown>, success: string) {
    try {
      await run();
      setNotice({ ok: true, text: success });
      reload();
    } catch (e) {
      setNotice({ ok: false, text: errorText(e) });
    }
  }
  // Success toasts fade out on their own; errors stay until dismissed.
  useEffect(() => {
    if (!notice?.ok) return;
    const timer = setTimeout(() => setNotice(undefined), 5000);
    return () => clearTimeout(timer);
  }, [notice]);
  const node = notice && <p role={notice.ok ? 'status' : 'alert'} className={notice.ok ? 'notice' : 'notice error'}>
    {notice.text} <button type="button" className="link" onClick={() => setNotice(undefined)} aria-label="Đóng thông báo">×</button>
  </p>;
  return { act, notice: node, done: (text: string) => { setNotice({ ok: true, text }); reload(); } };
}

export function LoadState({ loading, error, empty }: { loading: boolean; error?: string; empty: boolean }) {
  if (error) return <p role="alert" className="error">{error}</p>;
  if (loading) return <p role="status" className="muted">Đang tải…</p>;
  return empty ? <p className="muted">Không có dữ liệu phù hợp.</p> : null;
}

export function Pager({ page, pageSize, total, onPage }: { page: number; pageSize: number; total: number; onPage: (p: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  return <nav className="pager" aria-label="Phân trang">
    <button type="button" className="ghost" disabled={page <= 1} onClick={() => onPage(page - 1)}>‹ Trước</button>
    <span>Trang {page}/{pages} · {total} mục</span>
    <button type="button" className="ghost" disabled={page >= pages} onClick={() => onPage(page + 1)}>Sau ›</button>
  </nav>;
}

export const statusLabels: Record<AssetStatus, string> = {
  AVAILABLE: 'Sẵn sàng', RESERVED: 'Đã giữ chỗ', ON_LOAN: 'Đang cho mượn',
  MAINTENANCE: 'Bảo trì', LOST: 'Thất lạc', RETIRED: 'Ngừng sử dụng'
};

export function Badge({ tone, children }: { tone: string; children: ReactNode }) {
  return <span className={`badge ${tone}`}>{children}</span>;
}

export const formatDate = (iso: string) => new Date(iso).toLocaleString('vi-VN', { dateStyle: 'short', timeStyle: 'short' });
export const text = (form: FormData, name: string) => String(form.get(name) ?? '').trim();
export const optional = (form: FormData, name: string) => text(form, name) || null;
