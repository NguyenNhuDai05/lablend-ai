import { apiRequest } from '../../lib/api';

export type Notification = {
  id: string;
  eventType: string;
  aggregateId: string;
  title: string;
  message: string;
  createdAt: string;
  readAt: string | null;
};
export type NotificationPage = { items: Notification[]; total: number; page: number; pageSize: number };

export const notificationApi = {
  list: (page: number, pageSize = 20) => apiRequest<NotificationPage>(`/api/notifications?page=${page}&pageSize=${pageSize}`),
  markRead: (id: string) => apiRequest<Notification>(`/api/notifications/${id}/read`, { method: 'PATCH' }),
  unreadCount: () => apiRequest<{ count: number }>('/api/notifications/unread-count'),
};
