import type { ThemeConfig } from 'antd';

export const appTheme: ThemeConfig = {
  token: {
    colorPrimary: '#0a796a',
    colorPrimaryHover: '#086454',
    colorPrimaryActive: '#065344',
    colorInfo: '#0a796a',
    colorSuccess: '#218b69',
    colorWarning: '#d18a32',
    colorError: '#d35b54',
    colorBgLayout: '#f4f7f6',
    colorBgContainer: '#ffffff',
    colorText: '#1d3037',
    colorTextSecondary: '#526970',
    colorTextDescription: '#526970',
    colorTextPlaceholder: '#69787d',
    borderRadius: 12,
    fontFamily: 'Be Vietnam Pro, sans-serif',
    controlHeight: 42,
  },
  components: {
    Button: { fontWeight: 600, borderRadius: 10 },
    Card: { borderRadiusLG: 16 },
    Statistic: { colorTextDescription: '#526970' },
    Input: { borderRadius: 10 },
    Select: { borderRadius: 10 },
    Modal: { borderRadiusLG: 18 },
  },
};
