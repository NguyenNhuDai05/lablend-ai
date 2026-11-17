import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { App } from './App';
import { AuthProvider } from './auth/AuthContext';
import './styles.css';

// Re-apply the header theme toggle's choice before the first paint; storage may be blocked, then the system scheme wins.
try { const theme = localStorage.getItem('lablend-theme'); if (theme === 'light' || theme === 'dark') document.documentElement.dataset.theme = theme; } catch { /* use system scheme */ }

createRoot(document.getElementById('root')!).render(<StrictMode><BrowserRouter><AuthProvider><App /></AuthProvider></BrowserRouter></StrictMode>);
