import { Component, type ReactNode } from 'react';
import { Button, Result } from 'antd';
import { t } from '../i18n/runtime';

export default class ErrorBoundary extends Component<{children: ReactNode}, {failed: boolean}> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  render() {
    if (this.state.failed) return <Result status="error" title={t("Chưa tải được dữ liệu")} extra={<Button onClick={() => window.location.reload()}>{t("Thử lại")}</Button>} />;
    return this.props.children;
  }
}
