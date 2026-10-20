const field = (id, value) => {
  const length = Buffer.byteLength(value, 'utf8');
  if (length > 99) throw new Error('QR field too long');
  return id + String(length).padStart(2, '0') + value;
};
export function crc16(text) {
  let crc = 0xffff;
  for (const byte of Buffer.from(text)) {
    crc ^= byte << 8;
    for (let n = 0; n < 8; n++) crc = ((crc & 0x8000) ? (crc << 1) ^ 0x1021 : crc << 1) & 0xffff;
  }
  return crc.toString(16).toUpperCase().padStart(4, '0');
}
export function vietqr({ bankBin, bankAccount, amount, transferContent }) {
  if (!/^\d{6}$/.test(bankBin) || !/^[A-Za-z0-9]{1,32}$/.test(bankAccount) ||
      !Number.isSafeInteger(amount) || amount <= 0 || amount > 999999999999 || !/^[A-Z0-9 ]{1,25}$/.test(transferContent))
    throw new Error('Invalid bank transfer');
  const bank = field('00', bankBin) + field('01', bankAccount);
  const merchant = field('00', 'A000000727') + field('01', bank) + field('02', 'QRIBFTTA');
  const payload = field('00', '01') + field('01', '12') + field('38', merchant) + field('53', '704') +
    field('54', String(amount)) + field('58', 'VN') + field('62', field('08', transferContent)) + '6304';
  return payload + crc16(payload);
}
