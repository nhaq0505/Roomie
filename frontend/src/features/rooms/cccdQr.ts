import dayjs from 'dayjs';
import { legalCt01Template } from '../../shared/i18n/legalTemplates';

export interface ParsedCccdQr {
  citizenId: string;
  oldIdNumber: string | null;
  fullName: string;
  dateOfBirth: string | null;
  gender: string | null;
  permanentAddress: string | null;
  issuedDate: string | null;
  rawText: string;
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
  if (!cleaned.includes('|')) return null;

  const lines = cleaned.split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
  const candidateLine = lines.find((line) => line.includes('|')) ?? cleaned;
  const parts = candidateLine.split('|').map((item) => item.trim());
  if (parts.length < 5) return null;

  const citizenId = parts[0].replace(/\s+/g, '');
  if (!/^\d{9,12}$/.test(citizenId)) return null;

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
    };
  }

  return null;
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

  return null;
}
