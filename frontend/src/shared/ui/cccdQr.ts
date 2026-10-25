import dayjs from 'dayjs';
import { legalCt01Template } from '../i18n/legalTemplates';

export interface ParsedCccdQr {
  citizenId: string;
  oldIdNumber: string | null;
  fullName: string;
  dateOfBirth: string | null;
  gender: string | null;
  permanentAddress: string | null;
  issuedDate: string | null;
  rawText: string;
  documentType?: 'CCCD' | 'Passport';
  nationality?: string | null;
}

interface BarcodeDetectionResult {
  rawValue?: string;
}

interface BarcodeDetectorInstance {
  detect(source: ImageBitmapSource): Promise<BarcodeDetectionResult[]>;
}

interface WindowWithBarcodeDetector extends Window {
  BarcodeDetector?: {
    new (options?: { formats?: string[] }): BarcodeDetectorInstance;
  };
}

const ICAO_COUNTRY_NAMES: Record<string, string> = {
  KOR: `${''}Hàn Quốc (KOR)`,
  JPN: `${''}Nhật Bản (JPN)`,
  USA: `${''}Hoa Kỳ (USA)`,
  CHN: `${''}Trung Quốc (CHN)`,
  TWN: `${''}Đài Loan (TWN)`,
  GBR: `${''}Vương quốc Anh (GBR)`,
  FRA: `${''}Pháp (FRA)`,
  DEU: `${''}Đức (DEU)`,
  D: `${''}Đức (DEU)`,
  AUS: `${''}Úc (AUS)`,
  SGP: `${''}Singapore (SGP)`,
  THA: `${''}Thái Lan (THA)`,
  MYS: `${''}Malaysia (MYS)`,
  IDN: `${''}Indonesia (IDN)`,
  PHL: `${''}Philippines (PHL)`,
  IND: `${''}Ấn Độ (IND)`,
  CAN: `${''}Canada (CAN)`,
  RUS: `${''}Nga (RUS)`,
  VNM: `${''}Việt Nam (VNM)`,
  HKG: `${''}Hồng Kông (HKG)`,
  MAC: `${''}Ma Cao (MAC)`,
  LAO: `${''}Lào (LAO)`,
  KHM: `${''}Campuchia (KHM)`,
  MMR: `${''}Myanmar (MMR)`,
  NZL: `${''}New Zealand (NZL)`,
  ITA: `${''}Ý (ITA)`,
  ESP: `${''}Tây Ban Nha (ESP)`,
  NLD: `${''}Hà Lan (NLD)`,
  CHE: `${''}Thụy Sĩ (CHE)`,
  SWE: `${''}Thụy Điển (SWE)`,
};

export function computeMrzCheckDigit(input: string): string {
  const weights = [7, 3, 1];
  let sum = 0;
  for (let i = 0; i < input.length; i++) {
    const ch = input[i];
    let val = 0;
    if (ch >= '0' && ch <= '9') {
      val = ch.charCodeAt(0) - 48;
    } else if (ch >= 'A' && ch <= 'Z') {
      val = ch.charCodeAt(0) - 55;
    } else if (ch === '<') {
      val = 0;
    } else {
      return '';
    }
    sum += val * weights[i % 3];
  }
  return String(sum % 10);
}

function parseMrzBirthDate(yymmdd: string): string | null {
  if (!/^\d{6}$/.test(yymmdd)) return null;
  const yy = Number(yymmdd.slice(0, 2));
  const mm = yymmdd.slice(2, 4);
  const dd = yymmdd.slice(4, 6);
  const currentTwoDigitYear = new Date().getFullYear() % 100;
  const fullYear = yy <= currentTwoDigitYear ? 2000 + yy : 1900 + yy;
  return parseCccdDate(`${fullYear}-${mm}-${dd}`);
}

function isValidMrzExpiryDate(yymmdd: string): boolean {
  if (!/^\d{6}$/.test(yymmdd)) return false;
  const yy = Number(yymmdd.slice(0, 2));
  const mm = yymmdd.slice(2, 4);
  const dd = yymmdd.slice(4, 6);
  const fullYear = yy <= 70 ? 2000 + yy : 1900 + yy;
  return parseCccdDate(`${fullYear}-${mm}-${dd}`) !== null;
}

export function parsePassportMrz(rawInput: string): ParsedCccdQr | null {
  if (!rawInput) return null;
  const cleaned = rawInput.replace(/^\uFEFF/, '').trim().toUpperCase();
  if (!cleaned) return null;

  const rawLines = cleaned
    .split(/[\r\n]+/)
    .map((line) => line.replace(/\s+/g, ''))
    .filter(Boolean);

  const candidatePairs: Array<[string, string]> = [];
  if (rawLines.length === 1 && rawLines[0].length === 88) {
    candidatePairs.push([rawLines[0].slice(0, 44), rawLines[0].slice(44, 88)]);
  }
  for (let i = 0; i + 1 < rawLines.length; i++) {
    const l1 = rawLines[i];
    const l2 = rawLines[i + 1];
    if (l1.length === 44 && l2.length === 44) {
      candidatePairs.push([l1, l2]);
    }
  }

  for (const [line1, line2] of candidatePairs) {
    if (!/^P[A-Z0-9<][A-Z<]{42}$/.test(line1)) continue;
    if (!/^[A-Z0-9<]{44}$/.test(line2)) continue;

    const issuingState = line1.slice(2, 5).replace(/</g, '');
    if (!/^[A-Z]{1,3}$/.test(issuingState)) continue;

    const nameField = line1.slice(5, 44);
    const nameParts = nameField.split('<<');
    const surname = (nameParts[0] ?? '').replace(/<+/g, ' ').trim();
    const givenNames = nameParts
      .slice(1)
      .join(' ')
      .replace(/<+/g, ' ')
      .trim();
    const fullName = [surname, givenNames]
      .filter(Boolean)
      .join(' ')
      .replace(/\s+/g, ' ')
      .trim();
    if (!fullName || !/[A-Z]/.test(fullName)) continue;

    const passportField = line2.slice(0, 9);
    const passportCheck = line2[9];
    if (!/^\d$/.test(passportCheck) || computeMrzCheckDigit(passportField) !== passportCheck) {
      continue;
    }

    const citizenId = passportField.replace(/</g, '').trim();
    if (!/^([0-9]{9,12}|[A-Z]{1,3}[A-Z0-9]{5,9})$/.test(citizenId)) {
      continue;
    }

    const nationalityCode = line2.slice(10, 13).replace(/</g, '') || issuingState;
    if (!/^[A-Z]{1,3}$/.test(nationalityCode)) continue;

    const dobField = line2.slice(13, 19);
    const dobCheck = line2[19];
    if (!/^\d$/.test(dobCheck) || computeMrzCheckDigit(dobField) !== dobCheck) {
      continue;
    }
    const dateOfBirth = parseMrzBirthDate(dobField);
    if (!dateOfBirth) continue;

    const sexChar = line2[20];
    if (sexChar !== 'M' && sexChar !== 'F' && sexChar !== '<') continue;
    const gender = sexChar === 'M' || sexChar === 'F' ? normalizeCccdGender(sexChar) : null;

    const expiryField = line2.slice(21, 27);
    const expiryCheck = line2[27];
    if (!/^\d$/.test(expiryCheck) || computeMrzCheckDigit(expiryField) !== expiryCheck) {
      continue;
    }
    if (!isValidMrzExpiryDate(expiryField)) continue;

    const countryLabel = ICAO_COUNTRY_NAMES[nationalityCode] ?? nationalityCode;
    return {
      citizenId,
      oldIdNumber: null,
      fullName,
      dateOfBirth,
      gender,
      permanentAddress: `${''}Quốc tịch: ${countryLabel}`,
      issuedDate: null,
      rawText: `${line1}\n${line2}`,
      documentType: 'Passport',
      nationality: countryLabel,
    };
  }

  return null;
}

export function parseCccdDate(raw?: string | null): string | null {
  if (!raw) return null;
  const cleaned = raw.trim();
  if (!cleaned) return null;

  let dayStr = '';
  let monthStr = '';
  let yearStr = '';

  if (/^\d{8}$/.test(cleaned)) {
    dayStr = cleaned.slice(0, 2);
    monthStr = cleaned.slice(2, 4);
    yearStr = cleaned.slice(4, 8);
  } else {
    const dmyMatch = /^(\d{1,2})[/-](\d{1,2})[/-](\d{4})$/.exec(cleaned);
    if (dmyMatch) {
      dayStr = dmyMatch[1].padStart(2, '0');
      monthStr = dmyMatch[2].padStart(2, '0');
      yearStr = dmyMatch[3];
    } else {
      const ymdMatch = /^(\d{4})[/-](\d{1,2})[/-](\d{1,2})$/.exec(cleaned);
      if (ymdMatch) {
        yearStr = ymdMatch[1];
        monthStr = ymdMatch[2].padStart(2, '0');
        dayStr = ymdMatch[3].padStart(2, '0');
      } else {
        return null;
      }
    }
  }

  const day = Number(dayStr);
  const month = Number(monthStr);
  const year = Number(yearStr);
  if (year < 1900 || year > 2100 || month < 1 || month > 12 || day < 1 || day > 31) {
    return null;
  }

  const iso = `${yearStr}-${monthStr}-${dayStr}`;
  const parsed = dayjs(iso);
  if (!parsed.isValid() || parsed.year() !== year || parsed.month() + 1 !== month || parsed.date() !== day) {
    return null;
  }
  return iso;
}

export function normalizeCccdGender(raw?: string | null): string | null {
  if (!raw) return null;
  const ascii = raw
    .trim()
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/[\u0111\u0110]/g, 'd')
    .toLowerCase();

  if (ascii === 'nam' || ascii === 'male' || ascii === 'm') {
    return legalCt01Template.genderMale;
  }
  if (ascii === 'nu' || ascii === 'female' || ascii === 'f') {
    return legalCt01Template.genderFemale;
  }
  return null;
}

export function parseCccdQr(rawInput: string): ParsedCccdQr | null {
  if (!rawInput) return null;
  const cleaned = rawInput.replace(/^\uFEFF/, '').trim();
  if (!cleaned.includes('|')) {
    return parsePassportMrz(cleaned);
  }

  const lines = cleaned.split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
  const candidateLine = lines.find((line) => line.includes('|')) ?? cleaned;
  const parts = candidateLine.split('|').map((item) => item.trim());
  if (parts.length < 5) return parsePassportMrz(cleaned);

  const citizenId = parts[0].replace(/\s+/g, '');
  if (!/^\d{9,12}$/.test(citizenId)) return parsePassportMrz(cleaned);

  // Format 1 (standard 7 fields): citizenId | oldCmnd | fullName | dob | gender | address | issuedDate
  // Format 2 (6 fields without oldCmnd): citizenId | fullName | dob | gender | address | issuedDate
  const dobAt3 = parseCccdDate(parts[3]);
  const dobAt2 = parseCccdDate(parts[2]);

  if (dobAt3 !== null || (parts.length >= 7 && dobAt2 === null)) {
    const fullName = parts[2] ?? '';
    if (!fullName) return null;
    return {
      citizenId,
      oldIdNumber: parts[1] || null,
      fullName,
      dateOfBirth: dobAt3,
      gender: normalizeCccdGender(parts[4]),
      permanentAddress: parts[5] || null,
      issuedDate: parseCccdDate(parts[6]),
      rawText: candidateLine,
      documentType: 'CCCD',
      nationality: null,
    };
  }

  if (dobAt2 !== null || parts.length === 6) {
    const fullName = parts[1] ?? '';
    if (!fullName) return null;
    return {
      citizenId,
      oldIdNumber: null,
      fullName,
      dateOfBirth: dobAt2,
      gender: normalizeCccdGender(parts[3]),
      permanentAddress: parts[4] || null,
      issuedDate: parseCccdDate(parts[5]),
      rawText: candidateLine,
      documentType: 'CCCD',
      nationality: null,
    };
  }

  return parsePassportMrz(cleaned);
}

let jsQrLoader: Promise<(typeof import('jsqr'))['default']> | null = null;

async function getJsQr() {
  if (!jsQrLoader) {
    jsQrLoader = import('jsqr').then((mod) => mod.default);
  }
  return jsQrLoader;
}

function createBarcodeDetector(): BarcodeDetectorInstance | null {
  if (typeof window === 'undefined') return null;
  const win = window as WindowWithBarcodeDetector;
  if (!win.BarcodeDetector) return null;
  try {
    return new win.BarcodeDetector({ formats: ['qr_code'] });
  } catch {
    return null;
  }
}

function enhanceContrastInPlace(imageData: ImageData): void {
  const data = imageData.data;
  let sum = 0;
  const pixelCount = data.length / 4;
  for (let i = 0; i < data.length; i += 4) {
    sum += 0.299 * data[i] + 0.587 * data[i + 1] + 0.114 * data[i + 2];
  }
  const mean = sum / Math.max(1, pixelCount);
  const threshold = mean * 0.92;
  for (let i = 0; i < data.length; i += 4) {
    const lum = 0.299 * data[i] + 0.587 * data[i + 1] + 0.114 * data[i + 2];
    const stretched = lum < threshold ? Math.max(0, (lum / Math.max(1, threshold)) * 75) : Math.min(255, 180 + ((lum - threshold) / Math.max(1, 255 - threshold)) * 75);
    data[i] = stretched;
    data[i + 1] = stretched;
    data[i + 2] = stretched;
  }
}

export async function decodeCccdQrFromVideoFrame(
  video: HTMLVideoElement,
  canvas: HTMLCanvasElement,
): Promise<ParsedCccdQr | null> {
  const vw = video.videoWidth;
  const vh = video.videoHeight;
  if (!vw || !vh) return null;

  const detector = createBarcodeDetector();
  if (detector) {
    try {
      const results = await detector.detect(video);
      for (const item of results) {
        if (item.rawValue) {
          const parsed = parseCccdQr(item.rawValue);
          if (parsed) return parsed;
        }
      }
    } catch {
      // Fallback to jsQR below.
    }
  }

  const ctx = canvas.getContext('2d', { willReadFrequently: true });
  if (!ctx) return null;
  const jsQR = await getJsQr();

  // Pass 1: Full video frame scaled down to 960px max dimension.
  const maxDim = 960;
  const scale = Math.min(1, maxDim / Math.max(vw, vh));
  const w = Math.max(1, Math.round(vw * scale));
  const h = Math.max(1, Math.round(vh * scale));
  canvas.width = w;
  canvas.height = h;

  ctx.drawImage(video, 0, 0, w, h);
  const imageData = ctx.getImageData(0, 0, w, h);
  const code = jsQR(imageData.data, w, h, { inversionAttempts: 'attemptBoth' });
  if (code?.data) {
    const parsed = parseCccdQr(code.data);
    if (parsed) return parsed;
  }

  // Pass 2..4: Native-resolution center viewfinder crops (for small dense 12x12mm CCCD QR on cameras).
  const minSide = Math.min(vw, vh);
  const cropConfigs = [
    { ratio: 0.62, contrast: false, targetSize: 0 },
    { ratio: 0.45, contrast: true, targetSize: 0 },
    { ratio: 0.28, contrast: true, targetSize: 640 },
  ];
  for (const cfg of cropConfigs) {
    const srcSize = Math.max(1, Math.floor(minSide * cfg.ratio));
    const sx = Math.floor((vw - srcSize) / 2);
    const sy = Math.floor((vh - srcSize) / 2);
    const dstSize = cfg.targetSize > 0 ? Math.max(srcSize, cfg.targetSize) : Math.max(1, Math.min(960, srcSize));
    canvas.width = dstSize;
    canvas.height = dstSize;
    ctx.drawImage(video, sx, sy, srcSize, srcSize, 0, 0, dstSize, dstSize);

    if (detector && !cfg.contrast) {
      try {
        const cropResults = await detector.detect(canvas);
        for (const item of cropResults) {
          if (item.rawValue) {
            const parsed = parseCccdQr(item.rawValue);
            if (parsed) return parsed;
          }
        }
      } catch {
        // Ignore detector error on cropped canvas.
      }
    }

    const cropData = ctx.getImageData(0, 0, dstSize, dstSize);
    if (cfg.contrast) {
      enhanceContrastInPlace(cropData);
    }
    const cropCode = jsQR(cropData.data, dstSize, dstSize, { inversionAttempts: 'attemptBoth' });
    if (cropCode?.data) {
      const parsed = parseCccdQr(cropCode.data);
      if (parsed) return parsed;
    }
  }

  return null;
}

function loadImageElement(blob: Blob): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const url = URL.createObjectURL(blob);
    const img = new Image();
    img.onload = () => {
      URL.revokeObjectURL(url);
      resolve(img);
    };
    img.onerror = () => {
      URL.revokeObjectURL(url);
      if (typeof FileReader !== 'undefined' && typeof Blob !== 'undefined' && blob instanceof Blob) {
        const reader = new FileReader();
        reader.onload = () => {
          const fallbackImg = new Image();
          fallbackImg.onload = () => resolve(fallbackImg);
          fallbackImg.onerror = () => reject(new Error('Invalid image'));
          fallbackImg.src = String(reader.result || '');
        };
        reader.onerror = () => reject(new Error('Invalid image'));
        reader.readAsDataURL(blob);
        return;
      }
      reject(new Error('Invalid image'));
    };
    img.src = url;
  });
}

const MRZ_CHARSET = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789<';
const TEMPLATE_W = 16;
const TEMPLATE_H = 24;

interface MrzGlyphTemplate {
  ch: string;
  bits: Uint8Array;
  aspect: number;
}

let cachedMrzTemplates: MrzGlyphTemplate[] | null = null;

function resampleBinaryPatch(
  binary: Uint8Array,
  width: number,
  x0: number,
  y0: number,
  x1: number,
  y1: number,
): Uint8Array {
  const out = new Uint8Array(TEMPLATE_W * TEMPLATE_H);
  const bw = Math.max(1, x1 - x0 + 1);
  const bh = Math.max(1, y1 - y0 + 1);
  for (let ty = 0; ty < TEMPLATE_H; ty++) {
    const sy0 = y0 + Math.floor((ty * bh) / TEMPLATE_H);
    const sy1 = Math.max(sy0, y0 + Math.floor(((ty + 1) * bh) / TEMPLATE_H) - 1);
    for (let tx = 0; tx < TEMPLATE_W; tx++) {
      const sx0 = x0 + Math.floor((tx * bw) / TEMPLATE_W);
      const sx1 = Math.max(sx0, x0 + Math.floor(((tx + 1) * bw) / TEMPLATE_W) - 1);
      let ink = 0;
      let count = 0;
      for (let sy = sy0; sy <= sy1; sy++) {
        for (let sx = sx0; sx <= sx1; sx++) {
          ink += binary[sy * width + sx];
          count++;
        }
      }
      out[ty * TEMPLATE_W + tx] = count > 0 && ink * 2 >= count ? 1 : 0;
    }
  }
  return out;
}

function getMrzTemplates(): MrzGlyphTemplate[] | null {
  if (cachedMrzTemplates) return cachedMrzTemplates;
  if (typeof document === 'undefined') return null;

  const canvas = document.createElement('canvas');
  const cw = 48;
  const ch = 64;
  canvas.width = cw;
  canvas.height = ch;
  const ctx = canvas.getContext('2d', { willReadFrequently: true });
  if (!ctx || typeof ctx.fillText !== 'function') return null;

  const fonts = [
    'bold 40px "OCR-B", "Courier New", monospace',
    '40px "OCR-B", "Courier New", monospace',
    'bold 40px "Consolas", "Lucida Console", monospace',
  ];
  const templates: MrzGlyphTemplate[] = [];

  for (const font of fonts) {
    for (let i = 0; i < MRZ_CHARSET.length; i++) {
      const char = MRZ_CHARSET[i];
      ctx.fillStyle = '#ffffff';
      ctx.fillRect(0, 0, cw, ch);
      ctx.fillStyle = '#000000';
      ctx.font = font;
      ctx.textBaseline = 'top';
      ctx.fillText(char, 4, 8);

      const imgData = ctx.getImageData(0, 0, cw, ch).data;
      const bin = new Uint8Array(cw * ch);
      let x0 = cw;
      let y0 = ch;
      let x1 = -1;
      let y1 = -1;
      for (let y = 0; y < ch; y++) {
        for (let x = 0; x < cw; x++) {
          const lum = 0.299 * imgData[(y * cw + x) * 4] + 0.587 * imgData[(y * cw + x) * 4 + 1] + 0.114 * imgData[(y * cw + x) * 4 + 2];
          if (lum < 160) {
            bin[y * cw + x] = 1;
            if (x < x0) x0 = x;
            if (x > x1) x1 = x;
            if (y < y0) y0 = y;
            if (y > y1) y1 = y;
          }
        }
      }
      if (x1 < x0 || y1 < y0) continue;
      templates.push({
        ch: char,
        bits: resampleBinaryPatch(bin, cw, x0, y0, x1, y1),
        aspect: (x1 - x0 + 1) / Math.max(1, y1 - y0 + 1),
      });
    }
  }

  if (templates.length === 0) return null;
  cachedMrzTemplates = templates;
  return templates;
}

function binarizeAdaptive(imageData: ImageData, w: number, h: number): Uint8Array {
  const data = imageData.data;
  const integral = new Float64Array((w + 1) * (h + 1));
  const lum = new Uint8Array(w * h);

  for (let y = 0; y < h; y++) {
    let rowSum = 0;
    for (let x = 0; x < w; x++) {
      const idx = (y * w + x) * 4;
      const v = Math.round(0.299 * data[idx] + 0.587 * data[idx + 1] + 0.114 * data[idx + 2]);
      lum[y * w + x] = v;
      rowSum += v;
      integral[(y + 1) * (w + 1) + (x + 1)] = integral[y * (w + 1) + (x + 1)] + rowSum;
    }
  }

  const out = new Uint8Array(w * h);
  const rx = Math.max(8, Math.round(w * 0.025));
  const ry = Math.max(8, Math.round(h * 0.08));

  for (let y = 0; y < h; y++) {
    const y0 = Math.max(0, y - ry);
    const y1 = Math.min(h - 1, y + ry);
    for (let x = 0; x < w; x++) {
      const x0 = Math.max(0, x - rx);
      const x1 = Math.min(w - 1, x + rx);
      const count = (x1 - x0 + 1) * (y1 - y0 + 1);
      const sum =
        integral[(y1 + 1) * (w + 1) + (x1 + 1)] -
        integral[y0 * (w + 1) + (x1 + 1)] -
        integral[(y1 + 1) * (w + 1) + x0] +
        integral[y0 * (w + 1) + x0];
      const mean = sum / count;
      out[y * w + x] = lum[y * w + x] < mean * 0.86 - 8 ? 1 : 0;
    }
  }
  return out;
}

function allowedCharsForMrzPosition(lineIndex: 0 | 1, pos: number): string {
  if (lineIndex === 0) {
    if (pos === 0) return 'P';
    if (pos === 1) return '<ABCDEFGHIJKLMNOPQRSTUVWXYZ';
    return 'ABCDEFGHIJKLMNOPQRSTUVWXYZ<';
  }
  if (pos === 9 || (pos >= 13 && pos <= 19) || (pos >= 21 && pos <= 27)) {
    return '0123456789';
  }
  if (pos >= 10 && pos <= 12) {
    return 'ABCDEFGHIJKLMNOPQRSTUVWXYZ<';
  }
  if (pos === 20) {
    return 'MF<';
  }
  if (pos >= 42) {
    return '0123456789<';
  }
  return MRZ_CHARSET;
}

function scoreCellAgainstTemplates(
  cellBits: Uint8Array,
  cellAspect: number,
  templates: MrzGlyphTemplate[],
  allowed: string,
): Array<{ ch: string; score: number }> {
  const bestByChar = new Map<string, number>();
  const totalPixels = TEMPLATE_W * TEMPLATE_H;

  for (const tpl of templates) {
    if (!allowed.includes(tpl.ch)) continue;
    let matches = 0;
    for (let i = 0; i < totalPixels; i++) {
      if (cellBits[i] === tpl.bits[i]) matches++;
    }
    const aspectPenalty = Math.min(0.2, Math.abs(cellAspect - tpl.aspect) * 0.15);
    const score = matches / totalPixels - aspectPenalty;
    const prev = bestByChar.get(tpl.ch);
    if (prev === undefined || score > prev) {
      bestByChar.set(tpl.ch, score);
    }
  }

  return Array.from(bestByChar.entries())
    .map(([ch, score]) => ({ ch, score }))
    .sort((a, b) => b.score - a.score);
}

const OCR_SWAP_MAP: Record<string, string[]> = {
  '0': ['O', 'D', 'Q', '8'],
  O: ['0', 'Q', 'D'],
  D: ['0', 'O'],
  Q: ['0', 'O'],
  '1': ['I', 'L', '7'],
  I: ['1', 'L'],
  L: ['1', 'I'],
  '2': ['Z'],
  Z: ['2'],
  '5': ['S'],
  S: ['5'],
  '8': ['B', '0', '6'],
  B: ['8'],
  '6': ['G', '8'],
  G: ['6'],
};

function expandFieldCandidates(
  rankedPerPos: Array<Array<{ ch: string; score: number }>>,
  allowedCharset: string,
): string[][] {
  return rankedPerPos.map((ranked) => {
    const set = new Set<string>();
    for (const item of ranked.slice(0, 3)) {
      if (allowedCharset.includes(item.ch)) set.add(item.ch);
      for (const alt of OCR_SWAP_MAP[item.ch] ?? []) {
        if (allowedCharset.includes(alt)) set.add(alt);
      }
    }
    return Array.from(set);
  });
}

function repairFieldWithCheckDigit(
  rankedField: Array<Array<{ ch: string; score: number }>>,
  rankedCheck: Array<{ ch: string; score: number }>,
  fieldAllowed: string,
  validator?: (candidate: string) => boolean,
): { field: string; check: string } | null {
  const fieldOptions = expandFieldCandidates(rankedField, fieldAllowed);
  const checkOptions = expandFieldCandidates([rankedCheck], '0123456789')[0] ?? [];
  const primaryField = fieldOptions.map((opts) => opts[0] ?? '<').join('');

  for (const chk of checkOptions) {
    if (computeMrzCheckDigit(primaryField) === chk && (!validator || validator(primaryField))) {
      return { field: primaryField, check: chk };
    }
  }

  // Single-character substitution repair
  for (const chk of checkOptions) {
    for (let i = 0; i < fieldOptions.length; i++) {
      for (const alt of fieldOptions[i].slice(1)) {
        const mutated = primaryField.slice(0, i) + alt + primaryField.slice(i + 1);
        if (computeMrzCheckDigit(mutated) === chk && (!validator || validator(mutated))) {
          return { field: mutated, check: chk };
        }
      }
    }
  }

  // Two-character substitution repair
  for (const chk of checkOptions) {
    for (let i = 0; i < fieldOptions.length; i++) {
      for (const alt1 of fieldOptions[i].slice(1)) {
        for (let j = i + 1; j < fieldOptions.length; j++) {
          for (const alt2 of fieldOptions[j].slice(1)) {
            const mutated =
              primaryField.slice(0, i) +
              alt1 +
              primaryField.slice(i + 1, j) +
              alt2 +
              primaryField.slice(j + 1);
            if (computeMrzCheckDigit(mutated) === chk && (!validator || validator(mutated))) {
              return { field: mutated, check: chk };
            }
          }
        }
      }
    }
  }

  return null;
}

function extractCellsFromLine(
  bin: Uint8Array,
  w: number,
  y0: number,
  y1: number,
  xLeft: number,
  xRight: number,
  useConnectedRuns: boolean,
): Array<{ x0: number; y0: number; x1: number; y1: number } | null> | null {
  const lineH = y1 - y0 + 1;
  if (lineH < 6) return null;

  const cells: Array<{ x0: number; y0: number; x1: number; y1: number } | null> = [];

  if (useConnectedRuns) {
    const colInk = new Int32Array(w);
    for (let x = xLeft; x <= xRight; x++) {
      let c = 0;
      for (let y = y0; y <= y1; y++) {
        c += bin[y * w + x];
      }
      colInk[x] = c;
    }

    const runs: Array<{ x0: number; x1: number }> = [];
    let inRun = false;
    let runStart = xLeft;
    for (let x = xLeft; x <= xRight + 1; x++) {
      const active = x <= xRight && colInk[x] > 0;
      if (active && !inRun) {
        inRun = true;
        runStart = x;
      } else if (!active && inRun) {
        inRun = false;
        runs.push({ x0: runStart, x1: x - 1 });
      }
    }

    if (runs.length !== 44) return null;
    for (const r of runs) {
      let cy0 = y1;
      let cy1 = y0;
      for (let y = y0; y <= y1; y++) {
        for (let x = r.x0; x <= r.x1; x++) {
          if (bin[y * w + x]) {
            if (y < cy0) cy0 = y;
            if (y > cy1) cy1 = y;
          }
        }
      }
      cells.push(cy1 >= cy0 ? { x0: r.x0, y0: cy0, x1: r.x1, y1: cy1 } : null);
    }
    return cells;
  }

  const totalW = xRight - xLeft + 1;
  const pitch = totalW / 44;
  if (pitch < 4) return null;

  for (let i = 0; i < 44; i++) {
    const cx0 = Math.max(0, Math.floor(xLeft + i * pitch));
    const cx1 = Math.min(w - 1, Math.ceil(xLeft + (i + 1) * pitch) - 1);
    let gx0 = cx1;
    let gx1 = cx0;
    let gy0 = y1;
    let gy1 = y0;
    for (let y = y0; y <= y1; y++) {
      for (let x = cx0; x <= cx1; x++) {
        if (bin[y * w + x]) {
          if (x < gx0) gx0 = x;
          if (x > gx1) gx1 = x;
          if (y < gy0) gy0 = y;
          if (y > gy1) gy1 = y;
        }
      }
    }
    if (gx1 >= gx0 && gy1 >= gy0) {
      cells.push({ x0: gx0, y0: gy0, x1: gx1, y1: gy1 });
    } else {
      cells.push(null);
    }
  }
  return cells;
}

function recognizeMrzPair(
  bin: Uint8Array,
  w: number,
  line1Band: { y0: number; y1: number },
  line2Band: { y0: number; y1: number },
  templates: MrzGlyphTemplate[],
): ParsedCccdQr | null {
  const marginX = Math.max(2, Math.floor(w * 0.01));
  let xLeft = w;
  let xRight = -1;
  for (const band of [line1Band, line2Band]) {
    for (let y = band.y0; y <= band.y1; y++) {
      for (let x = marginX; x < w - marginX; x++) {
        if (bin[y * w + x]) {
          if (x < xLeft) xLeft = x;
          if (x > xRight) xRight = x;
        }
      }
    }
  }
  if (xRight - xLeft < 180) return null;

  for (const useConnectedRuns of [true, false]) {
    const cells1 = extractCellsFromLine(bin, w, line1Band.y0, line1Band.y1, xLeft, xRight, useConnectedRuns);
    const cells2 = extractCellsFromLine(bin, w, line2Band.y0, line2Band.y1, xLeft, xRight, useConnectedRuns);
    if (!cells1 || !cells2) continue;

    const ranked1: Array<Array<{ ch: string; score: number }>> = [];
    const ranked2: Array<Array<{ ch: string; score: number }>> = [];

    for (let i = 0; i < 44; i++) {
      const c1 = cells1[i];
      const allowed1 = allowedCharsForMrzPosition(0, i);
      if (!c1) {
        ranked1.push([{ ch: '<', score: 1 }]);
      } else {
        const bits1 = resampleBinaryPatch(bin, w, c1.x0, c1.y0, c1.x1, c1.y1);
        const aspect1 = (c1.x1 - c1.x0 + 1) / Math.max(1, c1.y1 - c1.y0 + 1);
        ranked1.push(scoreCellAgainstTemplates(bits1, aspect1, templates, allowed1));
      }

      const c2 = cells2[i];
      const allowed2 = allowedCharsForMrzPosition(1, i);
      if (!c2) {
        ranked2.push([{ ch: allowed2.includes('<') ? '<' : '0', score: 1 }]);
      } else {
        const bits2 = resampleBinaryPatch(bin, w, c2.x0, c2.y0, c2.x1, c2.y1);
        const aspect2 = (c2.x1 - c2.x0 + 1) / Math.max(1, c2.y1 - c2.y0 + 1);
        ranked2.push(scoreCellAgainstTemplates(bits2, aspect2, templates, allowed2));
      }
    }

    const line1Chars = ranked1.map((r) => r[0]?.ch ?? '<');
    line1Chars[0] = 'P';
    const line1Str = line1Chars.join('');

    const repairedPassport = repairFieldWithCheckDigit(
      ranked2.slice(0, 9),
      ranked2[9] ?? [{ ch: '0', score: 0 }],
      MRZ_CHARSET,
      (cand) => {
        const cleaned = cand.replace(/</g, '').trim();
        return /^([0-9]{9,12}|[A-Z]{1,3}[A-Z0-9]{5,9})$/.test(cleaned);
      },
    );
    if (!repairedPassport) continue;

    const repairedDob = repairFieldWithCheckDigit(
      ranked2.slice(13, 19),
      ranked2[19] ?? [{ ch: '0', score: 0 }],
      '0123456789',
      (cand) => parseMrzBirthDate(cand) !== null,
    );
    if (!repairedDob) continue;

    const repairedExpiry = repairFieldWithCheckDigit(
      ranked2.slice(21, 27),
      ranked2[27] ?? [{ ch: '0', score: 0 }],
      '0123456789',
      (cand) => isValidMrzExpiryDate(cand),
    );
    if (!repairedExpiry) continue;

    const natStr = ranked2
      .slice(10, 13)
      .map((r) => r[0]?.ch ?? '<')
      .join('');
    const sexStr = ranked2[20]?.[0]?.ch ?? '<';
    const tailStr = ranked2
      .slice(28, 44)
      .map((r) => r[0]?.ch ?? '<')
      .join('');

    const line2Str =
      repairedPassport.field +
      repairedPassport.check +
      natStr +
      repairedDob.field +
      repairedDob.check +
      sexStr +
      repairedExpiry.field +
      repairedExpiry.check +
      tailStr;

    const parsed = parsePassportMrz(`${line1Str}\n${line2Str}`);
    if (parsed) return parsed;
  }

  return null;
}

export async function decodePassportMrzFromImageBlob(blob: Blob): Promise<ParsedCccdQr | null> {
  try {
    const templates = getMrzTemplates();
    if (!templates) return null;

    const img = await loadImageElement(blob);
    const nw = img.naturalWidth || img.width;
    const nh = img.naturalHeight || img.height;
    if (!nw || !nh) return null;

    const canvas = document.createElement('canvas');
    const ctx = canvas.getContext('2d', { willReadFrequently: true });
    if (!ctx) return null;

    const bandConfigs = [
      { sy: 0.68, sh: 0.32 },
      { sy: 0.6, sh: 0.4 },
      { sy: 0, sh: 1 },
    ];

    for (const cfg of bandConfigs) {
      const srcY = Math.floor(nh * cfg.sy);
      const srcH = Math.max(1, Math.floor(nh * cfg.sh));
      const dstW = 1320;
      const dstH = Math.max(96, Math.min(480, Math.round((srcH * dstW) / nw)));

      canvas.width = dstW;
      canvas.height = dstH;
      ctx.drawImage(img, 0, srcY, nw, srcH, 0, 0, dstW, dstH);

      const imageData = ctx.getImageData(0, 0, dstW, dstH);
      const bin = binarizeAdaptive(imageData, dstW, dstH);

      const rowInk = new Int32Array(dstH);
      const marginX = Math.floor(dstW * 0.02);
      for (let y = 0; y < dstH; y++) {
        let sum = 0;
        for (let x = marginX; x < dstW - marginX; x++) {
          sum += bin[y * dstW + x];
        }
        rowInk[y] = sum;
      }

      const minRowInk = Math.max(12, Math.floor(dstW * 0.035));
      const lineBands: Array<{ y0: number; y1: number }> = [];
      let inBand = false;
      let bStart = 0;
      for (let y = 0; y <= dstH; y++) {
        const active = y < dstH && rowInk[y] >= minRowInk;
        if (active && !inBand) {
          inBand = true;
          bStart = y;
        } else if (!active && inBand) {
          inBand = false;
          const bEnd = y - 1;
          const bh = bEnd - bStart + 1;
          if (bh >= 8 && bh <= Math.floor(dstH * 0.48)) {
            lineBands.push({ y0: bStart, y1: bEnd });
          }
        }
      }

      for (let i = lineBands.length - 2; i >= 0; i--) {
        const parsed = recognizeMrzPair(bin, dstW, lineBands[i], lineBands[i + 1], templates);
        if (parsed) return parsed;
      }
    }

    return null;
  } catch {
    return null;
  }
}

interface CropRegion {
  sx: number;
  sy: number;
  sw: number;
  sh: number;
  maxDim: number;
  enhanceContrast?: boolean;
}

export async function decodeCccdQrFromImageBlob(blob: Blob): Promise<ParsedCccdQr | null> {
  const img = await loadImageElement(blob);
  const nw = img.naturalWidth || img.width;
  const nh = img.naturalHeight || img.height;
  if (!nw || !nh) return null;

  const detector = createBarcodeDetector();
  if (detector) {
    try {
      const results = await detector.detect(img);
      for (const item of results) {
        if (item.rawValue) {
          const parsed = parseCccdQr(item.rawValue);
          if (parsed) return parsed;
        }
      }
    } catch {
      // Continue to canvas multi-region scan.
    }
  }

  const jsQR = await getJsQr();
  const canvas = document.createElement('canvas');
  const ctx = canvas.getContext('2d', { willReadFrequently: true });
  if (!ctx) return null;

  // Multi-region scan tailored for Vietnamese CCCD photos (where QR is in the top-right corner, or rotated).
  const regions: CropRegion[] = [
    { sx: 0, sy: 0, sw: 1, sh: 1, maxDim: 1400 },
    { sx: 0.42, sy: 0, sw: 0.58, sh: 0.58, maxDim: 1100 }, // Top-right quadrant (standard CCCD QR location)
    { sx: 0.5, sy: 0, sw: 0.5, sh: 0.5, maxDim: 1100, enhanceContrast: true },
    { sx: 0.56, sy: 0.02, sw: 0.42, sh: 0.46, maxDim: 1200 }, // Tight top-right corner on landscape CCCD
    { sx: 0.56, sy: 0.02, sw: 0.42, sh: 0.46, maxDim: 1200, enhanceContrast: true },
    { sx: 0.45, sy: 0.18, sw: 0.52, sh: 0.5, maxDim: 1200 }, // Portrait phone photo (CCCD centered vertically)
    { sx: 0.45, sy: 0.18, sw: 0.52, sh: 0.5, maxDim: 1200, enhanceContrast: true },
    { sx: 0, sy: 0, sw: 0.58, sh: 0.58, maxDim: 1100 }, // Top-left (if mirrored/rotated)
    { sx: 0.42, sy: 0.42, sw: 0.58, sh: 0.58, maxDim: 1100 }, // Bottom-right (if rotated)
    { sx: 0, sy: 0.42, sw: 0.58, sh: 0.58, maxDim: 1100 }, // Bottom-left (if upside-down)
    { sx: 0.15, sy: 0.15, sw: 0.7, sh: 0.7, maxDim: 1200 }, // Center crop
    { sx: 0.22, sy: 0.22, sw: 0.56, sh: 0.56, maxDim: 1200, enhanceContrast: true }, // Tight center crop
    { sx: 0, sy: 0, sw: 1, sh: 1, maxDim: 2200 }, // High-res full pass
    { sx: 0, sy: 0, sw: 1, sh: 1, maxDim: 1400, enhanceContrast: true }, // High-contrast full pass
  ];

  for (const region of regions) {
    const srcX = Math.floor(nw * region.sx);
    const srcY = Math.floor(nh * region.sy);
    const srcW = Math.max(1, Math.floor(nw * region.sw));
    const srcH = Math.max(1, Math.floor(nh * region.sh));
    const scale = Math.min(2, region.maxDim / Math.max(srcW, srcH));
    const dstW = Math.max(1, Math.round(srcW * scale));
    const dstH = Math.max(1, Math.round(srcH * scale));

    canvas.width = dstW;
    canvas.height = dstH;
    ctx.drawImage(img, srcX, srcY, srcW, srcH, 0, 0, dstW, dstH);

    if (detector && (region.sx !== 0 || region.sy !== 0)) {
      try {
        const results = await detector.detect(canvas);
        for (const item of results) {
          if (item.rawValue) {
            const parsed = parseCccdQr(item.rawValue);
            if (parsed) return parsed;
          }
        }
      } catch {
        // Ignore detector error on canvas crop.
      }
    }

    const imageData = ctx.getImageData(0, 0, dstW, dstH);
    if (region.enhanceContrast) {
      enhanceContrastInPlace(imageData);
    }
    const code = jsQR(imageData.data, dstW, dstH, { inversionAttempts: 'attemptBoth' });
    if (code?.data) {
      const parsed = parseCccdQr(code.data);
      if (parsed) return parsed;
    }
  }

  return await decodePassportMrzFromImageBlob(blob);
}
