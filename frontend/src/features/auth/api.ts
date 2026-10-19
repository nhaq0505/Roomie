import { post, request } from '../../shared/http/client';
import type { RegistrationVerification, User } from './types';


export const authApi = {
    login: (email: string, password: string) => request<User>('/api/auth/login', post({ email, password })),
    verifyRegistration: (challengeToken: string, code: string) => request<User>('/api/auth/verify-registration', post({ challengeToken, code })),
    resendRegistration: (challengeToken: string) => request<RegistrationVerification>('/api/auth/resend-registration', post({ challengeToken })),
    me: () => request<User>('/api/auth/me'),
    changePassword: (body: { currentPassword: string; newPassword: string; confirmPassword?: string }) => request<{ success: boolean }>('/api/auth/change-password', post(body)),
    resetPassword: (body: { email: string; token: string; newPassword: string; confirmPassword: string }) => request<{ success: boolean }>('/api/auth/reset-password', post(body)),
    logout: () => request<void>('/api/auth/logout', post()),
    register: (body: { name: string; email: string; password: string; phone: string }) => request<User | RegistrationVerification>('/api/auth/register', post(body)),
  };
