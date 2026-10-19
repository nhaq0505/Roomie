import type { ThemeConfig } from 'antd';

export const appTheme: ThemeConfig = {
  token: {
    colorPrimary: '#0f9d83',
    colorInfo: '#0f9d83',
    colorSuccess: '#218b69',
    colorWarning: '#d18a32',
    colorError: '#d35b54',
    colorBgLayout: '#f4f7f6',
    colorBgContainer: '#ffffff',
    colorText: '#1d3037',
    colorTextSecondary: '#718188',
    borderRadius: 12,
    fontFamily: 'DM Sans, sans-serif',
    controlHeight: 42,
  },
  components: {
    Button: { fontWeight: 600, borderRadius: 10 },
    Card: { borderRadiusLG: 16 },
    Input: { borderRadius: 10 },
    Select: { borderRadius: 10 },
    Modal: { borderRadiusLG: 18 },
  },
};
