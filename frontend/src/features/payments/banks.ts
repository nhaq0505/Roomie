import type { PaymentBank } from './types';
import rawBanks from './vietqr-banks.json';

export const VIETQR_BANKS: readonly PaymentBank[] = rawBanks as PaymentBank[];

export function formatBankLabel(bin?: string | null): string {
  if (!bin) return '';
  const bank = VIETQR_BANKS.find(b => b.bin === bin);
  return bank ? `${bank.shortName} (${bank.bin})` : bin;
}
