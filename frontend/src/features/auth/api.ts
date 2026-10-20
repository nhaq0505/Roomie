import { post, request } from '../../shared/http/client';
import type { RegistrationVerification, User } from './types';


export const authApi = {
    forgotPassword: (email: string) => request<import('./types').PasswordRecoveryChallenge>('/api/auth/forgot-password', post({ email })),
    verifyPasswordRecovery: (challengeToken: string, code: string) => request<import('./types').PasswordRecoveryGrant>('/api/auth/verify-password-recovery', post({ challengeToken, code })),
    completePasswordRecovery: (resetToken: string, newPassword: string, confirmPassword: string) => request<{ success: boolean }>('/api/auth/complete-password-recovery', post({ resetToken, newPassword, confirmPassword })),
    login: (email: string, password: string) => request<User>('/api/auth/login', post({ email, password })),
    verifyRegistration: (challengeToken: string, code: string) => request<User>('/api/auth/verify-registration', post({ challengeToken, code })),
    resendRegistration: (challengeToken: string) => request<RegistrationVerification>('/api/auth/resend-registration', post({ challengeToken })),
    me: () => request<User>('/api/auth/me'),
    changePassword: (body: { currentPassword: string; newPassword: string; confirmPassword?: string }) => request<{ success: boolean }>('/api/auth/change-password', post(body)),
    resetPassword: (body: { email: string; token: string; newPassword: string; confirmPassword: string }) => request<{ success: boolean }>('/api/auth/reset-password', post(body)),
    logout: () => request<void>('/api/auth/logout', post()),
    register: (body: { name: string; email: string; password: string; phone: string; attribution?: import('../../shared/analytics').RegistrationAttributionDto }) => request<User | RegistrationVerification>('/api/auth/register', post(body)),
  };
