import { Link } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
export function HomePage() {
  const { user } = useAuth();
  return <section className="hero"><p className="eyebrow">QUẢN LÝ THIẾT BỊ PHÒNG THÍ NGHIỆM</p><h1>Mượn đúng thiết bị.<br />Theo dõi thật rõ ràng.</h1><p className="intro">LabLend kết nối sinh viên và nhân viên phòng lab trong một quy trình mượn, bàn giao và hoàn trả minh bạch.</p>{!user && <div className="hero-actions"><Link className="button-link" to="/register">Bắt đầu sử dụng</Link><Link to="/login">Tôi đã có tài khoản</Link></div>}</section>;
}
