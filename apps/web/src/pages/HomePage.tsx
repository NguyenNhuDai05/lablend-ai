import { Link } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
export function HomePage() {
  const { user } = useAuth();
  return <section className="hero"><p className="eyebrow">QUẢN LÝ THIẾT BỊ PHÒNG THÍ NGHIỆM</p><h1>Mượn đúng thiết bị.<br />Theo dõi thật rõ ràng.</h1><p className="intro">LabLend kết nối sinh viên và nhân viên phòng lab trong một quy trình mượn, bàn giao và hoàn trả minh bạch.</p><div className="hero-actions"><Link className="button-link" to="/catalog">Khám phá thiết bị</Link>{!user ? <Link to="/register">Tạo tài khoản</Link> : <Link to={user.role === 'STUDENT' ? '/loans' : '/staff/loans'}>Mở phiếu mượn</Link>}</div></section>;
}
