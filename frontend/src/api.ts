// Compatibility entry point. New modules import feature APIs and shared helpers directly.
import { authApi } from './features/auth';
import { dashboardApi } from './features/dashboard';
import { buildingsApi } from './features/buildings';
import { roomsApi } from './features/rooms';
import { readingsApi } from './features/readings';
import { invoicesApi } from './features/invoices';
import { ownersApi, marketersApi } from './features/admin';
import { marketerApi } from './features/marketer';
import { subscriptionApi } from './features/subscription';
import { paymentSettingsApi } from './features/payments';
import { tenantApi } from './features/tenant';
import { reportsApi } from './features/reports';

export const api = {
  auth: authApi,
  dashboard: dashboardApi,
  buildings: buildingsApi,
  rooms: roomsApi,
  readings: readingsApi,
  invoices: invoicesApi,
  owners: ownersApi,
  marketers: marketersApi,
  marketer: marketerApi,
  subscription: subscriptionApi,
  paymentSettings: paymentSettingsApi,
  tenant: tenantApi,
  reports: reportsApi,
};

export type * from './features/auth';
export type * from './features/subscription';
export type * from './features/buildings';
export type * from './features/rooms';
export type * from './features/dashboard';
export type * from './features/readings';
export type * from './features/invoices';
export type * from './features/reports';
export type * from './features/admin';
export type * from './features/marketer';
export type * from './features/payments';
export type * from './features/tenant';
export { ApiError, listFrom } from './shared/http/client';
export { formatVnd, currentPeriod, periodLabel } from './shared/format';
