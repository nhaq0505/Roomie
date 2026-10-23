import { t } from '../../shared/i18n/runtime';
import type { ExpenseCategory, RecurringSkipReason } from './types';

export const expenseCategories: ExpenseCategory[] = ['Electricity', 'Water', 'Internet', 'Repair', 'Cleaning', 'Salary', 'LeaseRent', 'Tax', 'Other'];

export function expenseCategoryLabel(category: ExpenseCategory | string) {
  switch (category) {
    case 'Electricity': return t("Điện tổng (EVN)");
    case 'Water': return t("Nước tổng");
    case 'Internet': return t("Internet / truyền hình");
    case 'Repair': return t("Sửa chữa, bảo trì");
    case 'Cleaning': return t("Vệ sinh, rác");
    case 'Salary': return t("Lương quản lý / nhân công");
    case 'LeaseRent': return t("Tiền thuê nhà (cho thuê lại)");
    case 'Tax': return t("Thuế, phí, lệ phí");
    default: return t("Khác");
  }
}

export const expenseCategoryColor: Record<ExpenseCategory, string> = {
  Electricity: 'gold', Water: 'blue', Internet: 'geekblue', Repair: 'volcano', Cleaning: 'green',
  Salary: 'purple', LeaseRent: 'magenta', Tax: 'red', Other: 'default',
};

export const expenseCategoryOptions = () => expenseCategories.map((value) => ({ value, label: expenseCategoryLabel(value) }));

export function skipReasonLabel(reason: RecurringSkipReason | null) {
  switch (reason) {
    case 'ProRequired': return t("Tạm ngừng vì tài khoản không còn gói Pro");
    case 'LocationArchived': return t("Tạm ngừng vì dãy hoặc phòng đã lưu trữ");
    case 'Ended': return t("Đã kết thúc");
    case 'Paused': return t("Đang tạm ngừng");
    case 'NotStarted': return t("Chưa đến kỳ bắt đầu");
    default: return null;
  }
}

export function formatBytes(bytes: number) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

export function todayInVietnam() {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Ho_Chi_Minh', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date());
  const part = (type: string) => parts.find((item) => item.type === type)?.value ?? '';
  return `${part('year')}-${part('month')}-${part('day')}`;
}

export function monthRange(period: string) {
  const [year, month] = period.split('-').map(Number);
  const lastDay = new Date(Date.UTC(year, month, 0)).getUTCDate();
  return [`${period}-01`, `${period}-${String(lastDay).padStart(2, '0')}`] as const;
}
