import { useState } from 'react';
import { checkGateway } from './lib/api';

export function App() {
  const [state, setState] = useState<'idle' | 'loading' | 'success' | 'error'>('idle');
  const [message, setMessage] = useState('Chưa kiểm tra kết nối.');
  async function check() {
    setState('loading');
    setMessage('Đang kiểm tra kết nối…');
    try {
      const health = await checkGateway();
      setState('success');
      setMessage(`Gateway: ${health.status}.`);
    } catch (error) {
      setState('error');
      setMessage(error instanceof Error ? error.message : 'Không thể kết nối Gateway.');
    }
  }
  return <main>
    <p className="eyebrow">PRN232 · SPRINT 0</p>
    <h1>LabLend AI</h1>
    <p className="intro">Quản lý mượn và trả thiết bị phòng thí nghiệm.</p>
    <section aria-labelledby="foundation-title">
      <h2 id="foundation-title">Nền tảng đã sẵn sàng để phát triển</h2>
      <p>Đây là giao diện khởi đầu. Đăng nhập, danh mục và quy trình mượn trả sẽ được nhóm triển khai trong các sprint tiếp theo.</p>
      <button onClick={() => void check()} disabled={state === 'loading'}>
        {state === 'loading' ? 'Đang kết nối…' : 'Kiểm tra kết nối'}
      </button>
      <p role="status" className={state === 'error' ? 'error' : ''}>{message}</p>
    </section>
  </main>;
}
