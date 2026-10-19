import { t } from '../../shared/i18n/runtime';

export function statusLabel(status: string) {
  if (status === 'Paid') return t("Đã thanh toán");
  if (status === 'Partial') return t("Thanh toán một phần");
  return t("Chưa thanh toán");
}
export function statusClass(status: string) {
  if (status === 'Paid') return 'status-paid';
  if (status === 'Partial') return 'status-partial';
  return 'status-unpaid';
}
