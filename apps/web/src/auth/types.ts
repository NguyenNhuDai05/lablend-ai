export const roles = ['STUDENT', 'LAB_STAFF', 'ADMIN'] as const;
export type UserRole = typeof roles[number];
export type CurrentUser = { id: string; email: string; displayName?: string; role: UserRole };
export type LoginRequest = { email: string; password: string };
export type RegisterRequest = { email: string; password: string; confirmPassword: string; displayName: string };
export type CurrentUserResponse = {
  id: string; email: string; displayName: string; isActive: boolean; roles: UserRole[];
  createdAt: string; updatedAt: string;
};
export type AuthResponse = { accessToken: string; tokenType: string; expiresAt: string; user: CurrentUser };
export type AuthSessionResponse = Omit<AuthResponse, 'user'> & { user: CurrentUserResponse };
