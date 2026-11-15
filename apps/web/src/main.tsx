import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { StaffApp } from './features/staff/StaffApp';
import './styles.css';

// ponytail: path switch until M3's router lands; then register StaffApp under /staff there.
const Root = location.pathname.startsWith('/staff') ? StaffApp : App;
createRoot(document.getElementById('root')!).render(<StrictMode><Root /></StrictMode>);
