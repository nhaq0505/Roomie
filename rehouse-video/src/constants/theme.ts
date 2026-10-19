export const REHOUSE_THEME = {
  // Brand colors extracted from frontend/src/shared/styles/foundation.css & frontend/src/features/rooms/theme.ts
  teal: '#109d83',
  tealDark: '#0a796a',
  tealHover: '#086454',
  tealLight: '#eaf6ef',
  tealGlow: 'rgba(16, 157, 131, 0.35)',

  navy: '#15343e',
  navyDark: '#0e232a',
  navyCard: '#1a3e4a',

  ink: '#19333b',
  muted: '#526970',
  mutedLight: '#71838a',
  border: '#e6edeb',
  controlBorder: '#c8d7d1',

  canvas: '#f4f7f6',
  surface: '#ffffff',
  hover: '#edf5f1',

  amber: '#cb8b37',
  amberLight: '#fef7ec',
  red: '#c95854',
  redLight: '#fdf2f1',
  green: '#248364',
  greenLight: '#e8f7f1',

  fontDisplay: "'Manrope', 'Plus Jakarta Sans', system-ui, -apple-system, sans-serif",
  fontBody: "'DM Sans', system-ui, -apple-system, sans-serif",
  fontMono: "'SF Mono', Menlo, Consolas, monospace",
} as const;

export interface RoomSample {
  id: string;
  code: string;
  name: string;
  tenantName: string;
  phone: string;
  buildingName: string;
  status: 'occupied' | 'vacant';
  price: number;
  prevReading: number;
  newReading: number;
  electricRate: number;
  waterAmount: number;
}

export const MOCK_ROOMS: RoomSample[] = [
  {
    id: 'room-101',
    code: 'P.101',
    name: 'Phòng 101 - Ban công thoáng',
    tenantName: 'Trần Minh Đức',
    phone: '0912 345 678',
    buildingName: 'Tòa nhà Nguyễn Văn Cừ (Q.5)',
    status: 'occupied',
    price: 3500000,
    prevReading: 1210,
    newReading: 1340,
    electricRate: 3500,
    waterAmount: 180000,
  },
  {
    id: 'room-201',
    code: 'P.201',
    name: 'Phòng 201 - Studio Full NT',
    tenantName: 'Nguyễn Văn An',
    phone: '0988 765 432',
    buildingName: 'Tòa nhà Nguyễn Văn Cừ (Q.5)',
    status: 'occupied',
    price: 3200000,
    prevReading: 1420,
    newReading: 1565,
    electricRate: 3500,
    waterAmount: 200000,
  },
  {
    id: 'room-202',
    code: 'P.202',
    name: 'Phòng 202 - Gác lửng cao',
    tenantName: 'Lê Hoàng Nam',
    phone: '0903 112 233',
    buildingName: 'Tòa nhà Nguyễn Văn Cừ (Q.5)',
    status: 'occupied',
    price: 3600000,
    prevReading: 980,
    newReading: 1110,
    electricRate: 3500,
    waterAmount: 200000,
  },
  {
    id: 'room-301',
    code: 'P.301',
    name: 'Phòng 301 - Căn góc 2 cửa sổ',
    tenantName: 'Đang trống',
    phone: '—',
    buildingName: 'Tòa nhà Nguyễn Văn Cừ (Q.5)',
    status: 'vacant',
    price: 3400000,
    prevReading: 650,
    newReading: 650,
    electricRate: 3500,
    waterAmount: 0,
  },
];

export const MOCK_INVOICE_P201 = {
  invoiceId: 'INV-2026-10-201',
  roomCode: 'P.201',
  building: 'Tòa nhà Nguyễn Văn Cừ (Q.5)',
  tenantName: 'Nguyễn Văn An',
  period: 'Tháng 10/2026',
  rent: 3200000,
  electricKwh: 145,
  electricRate: 3500,
  electricTotal: 507500,
  waterM3: 8,
  waterRate: 25000,
  waterTotal: 200000,
  serviceFee: 0,
  total: 3907500,
  payOsAccount: 'REHOUSE 201 T10',
};
