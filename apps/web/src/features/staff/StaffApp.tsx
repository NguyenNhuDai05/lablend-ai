import { Navigate } from 'react-router-dom';

/** Compatibility entry; authentication and staff authorization live in App routes. */
export function StaffApp() {
  return <Navigate to="/staff" replace />;
}
