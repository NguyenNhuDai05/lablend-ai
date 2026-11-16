import type { CurrentUser, CurrentUserResponse, UserRole } from './types';

// Preserve the routed UI's single-role model while consuming Identity's roles[] DTO.
export function toCurrentUser(response: CurrentUserResponse): CurrentUser {
  const order: UserRole[] = ['ADMIN', 'LAB_STAFF', 'STUDENT'];
  const role = Array.isArray(response.roles) ? order.find(value => response.roles.includes(value)) : undefined;
  if (!response.isActive || !role) throw new Error('Tài khoản không hoạt động hoặc không có quyền hợp lệ.');
  return { id: response.id, email: response.email, displayName: response.displayName, role };
}
