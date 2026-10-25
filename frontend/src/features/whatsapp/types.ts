export interface WhatsAppSettings { scheduleEnabled: boolean; dayOfMonth: number; hour: number; minute: number; bankBin: string; bankAccount: string; bankAccountName: string; gatewayEnabled: boolean; }
export interface WhatsAppConnection { status: string; qrImage?: string | null; }
export interface WhatsAppReminder { invoiceId: string; roomName: string; period: string; status: string; detail?: string | null; queuedAtMs: number; lastAttemptAtMs: number; sentAtMs?: number | null; }
export interface WhatsAppHistory { items: WhatsAppReminder[]; total: number; page: number; pageSize: number; }
