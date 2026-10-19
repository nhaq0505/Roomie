import type { ThemeConfig } from 'antd';

// Pilot contract: keep Roomie's green; use one font and shared control geometry.
export const roomDesignTokens = {
  accent: '#0a796a',
  accentHover: '#086454',
  text: '#19333b',
  muted: '#526970',
  border: '#e6edeb',
  controlBorder: '#c8d7d1',
  surface: '#ffffff',
  hover: '#f4f7f6',
  font: 'DM Sans, sans-serif',
} as const;

export function roomTheme(compact: boolean): ThemeConfig {
  const height = compact ? 44 : 40;
  return {
    token: {
      colorPrimary: roomDesignTokens.accent,
      colorPrimaryHover: roomDesignTokens.accentHover,
      colorText: roomDesignTokens.text,
      colorTextSecondary: roomDesignTokens.muted,
      colorBorder: roomDesignTokens.controlBorder,
      colorBgContainer: roomDesignTokens.surface,
      fontFamily: roomDesignTokens.font,
      fontSize: 14,
      borderRadius: 12,
      controlHeight: height,
    },
    components: {
      Button: { borderRadius: 12, fontWeight: 500, primaryShadow: 'none', defaultShadow: 'none' },
      Input: { borderRadius: 12, activeShadow: '0 0 0 2px rgba(10,121,106,.1)' },
      InputNumber: { borderRadius: 12, activeShadow: '0 0 0 2px rgba(10,121,106,.1)' },
      Select: { borderRadius: 12, optionSelectedBg: '#edf5f1', optionSelectedColor: roomDesignTokens.text },
      Table: { headerBg: roomDesignTokens.hover, headerColor: roomDesignTokens.muted, borderColor: roomDesignTokens.border, rowHoverBg: '#f8faf9', cellPaddingBlock: 16, cellPaddingInline: 16 },
      Modal: { borderRadiusLG: 16 },
    },
  };
}
