export interface ZaloSettings { scheduleEnabled: boolean; dayOfMonth: number; hour: number; minute: number; bankBin: string; bankAccount: string; bankAccountName: string; gatewayEnabled: boolean; }
export interface ZaloConnection { status: string; qrImage?: string | null; }
export interface ZaloReminder { invoiceId: string; roomName: string; period: string; status: string; detail?: string | null; queuedAtMs: number; lastAttemptAtMs: number; sentAtMs?: number | null; }
export interface ZaloHistory { items: ZaloReminder[]; total: number; page: number; pageSize: number; }
