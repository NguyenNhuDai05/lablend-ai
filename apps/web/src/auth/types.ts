export const roles = ['STUDENT', 'LAB_STAFF', 'ADMIN'] as const;
export type UserRole = typeof roles[number];
export type CurrentUser = { id: string; email: string; displayName?: string; role: UserRole };
export type LoginRequest = { email: string; password: string };
export type RegisterRequest = { email: string; password: string; displayName: string };
export type AuthResponse = { accessToken: string; user: CurrentUser };
