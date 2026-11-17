import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { useResource } from '../components/Workflow';
import { Icon } from '../components/Icon';
import { catalogApi } from '../features/catalog/api';

// ponytail: stock totals cover the first 100 equipment types (API max page); add a summary endpoint if the catalog outgrows that.
const loadStats = async (signal: AbortSignal) => {
  const types = await catalogApi.list({ pageSize: 100 }, signal);
  const stock = await catalogApi.availability(types.items.map(t => t.id), signal);
  return { types: types.total, available: stock.reduce((n, s) => n + s.available, 0), total: stock.reduce((n, s) => n + s.total, 0) };
};

const steps = [
  { title: 'Tìm thiết bị', text: 'Lọc theo danh mục, xem thông số và số lượng còn sẵn trong kho.' },
  { title: 'Gửi yêu cầu mượn', text: 'Chọn số lượng, ghi mục đích và hạn trả; nhân viên lab duyệt phiếu.' },
  { title: 'Nhận và hoàn trả', text: 'Nhận thiết bị tại phòng lab, theo dõi hạn trả và lịch sử ngay trên web.' },
];

export function HomePage() {
  const { user } = useAuth();
  const navigate = useNavigate();
  const stats = useResource('home-stats', loadStats);
  return <>
    <section className="hero">
      <p className="eyebrow">QUẢN LÝ THIẾT BỊ PHÒNG THÍ NGHIỆM</p>
      <h1>Mượn đúng thiết bị.<br />Theo dõi thật rõ ràng.</h1>
      <p className="intro">LabLend kết nối sinh viên và nhân viên phòng lab trong một quy trình mượn, bàn giao và hoàn trả minh bạch.</p>
      <form className="hero-search" role="search" onSubmit={e => { e.preventDefault(); const keyword = String(new FormData(e.currentTarget).get('keyword') ?? '').trim(); navigate(keyword ? `/catalog?keyword=${encodeURIComponent(keyword)}` : '/catalog'); }}>
        <Icon name="search" />
        <input name="keyword" type="search" placeholder="Arduino, máy hiện sóng…" aria-label="Từ khóa thiết bị" />
        <button>Tìm thiết bị</button>
      </form>
      <div className="hero-actions"><Link className="button-link" to="/catalog">Khám phá thiết bị</Link>{!user ? <Link to="/register">Tạo tài khoản</Link> : <Link to={user.role === 'STUDENT' ? '/loans' : '/staff/loans'}>Mở phiếu mượn</Link>}</div>
    </section>
    {stats.data && <dl className="home-stats" aria-label="Tổng quan kho thiết bị">
      <div><dt>Loại thiết bị</dt><dd>{stats.data.types}</dd></div>
      <div><dt>Thiết bị sẵn sàng</dt><dd>{stats.data.available}</dd></div>
      <div><dt>Tổng số thiết bị</dt><dd>{stats.data.total}</dd></div>
    </dl>}
    <section className="home-steps" aria-labelledby="home-steps-title">
      <h2 id="home-steps-title">Quy trình 3 bước</h2>
      <ol>{steps.map(s => <li key={s.title}><h3>{s.title}</h3><p>{s.text}</p></li>)}</ol>
    </section>
  </>;
}
