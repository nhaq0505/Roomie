import { request } from '../../shared/http/client';
import type { DashboardSummary } from './types';


export const dashboardApi = () => request<DashboardSummary>('/api/dashboard');
