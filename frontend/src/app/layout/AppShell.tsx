import RoomieMark from '../../shared/ui/RoomieMark';
import ChangePasswordModal from '../../features/auth/ChangePasswordModal';
import LanguageSelector from '../../shared/i18n/LanguageSelector';
import { t } from '../../shared/i18n/runtime';
import { useEffect, useState, type ReactNode } from 'react';
import { App, Avatar, Button, Drawer, Dropdown, Layout, Menu, Space, Typography } from 'antd';
import type { MenuProps } from 'antd';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import {
  AppstoreOutlined, BankOutlined, CalculatorOutlined, BarChartOutlined, CreditCardOutlined, MessageOutlined,
  SettingOutlined, DownOutlined, CrownOutlined, FileTextOutlined, HomeOutlined, LogoutOutlined, MenuOutlined, UserOutlined,
} from '@ant-design/icons';
import { useAuth } from '../../features/auth/session';

const { Sider, Header, Content } = Layout;



export default function AppShell({ children }: { children: ReactNode }) {
  const { message } = App.useApp();
  const { user, signOut } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const [mobileOpen, setMobileOpen] = useState(false);
  const [passwordOpen, setPasswordOpen] = useState(false);
  const [openKeys, setOpenKeys] = useState<string[]>(location.pathname.startsWith('/settings/') ? ['settings'] : []);
  useEffect(() => {
    if (location.pathname.startsWith('/settings/')) setOpenKeys(keys => keys.includes('settings') ? keys : [...keys, 'settings']);
  }, [location.pathname]);
  const settingsItems = [
    { key: '/settings/payment', icon: <CreditCardOutlined />, label: t("Thanh toán") },
    { key: '/settings/zalo', icon: <MessageOutlined />, label: 'Zalo' },
  ];
  useEffect(() => { window.scrollTo({ top: 0, left: 0 }); }, [location.pathname]);
const ownerItems = [
  { key: '/dashboard', icon: <AppstoreOutlined />, label: t("Tổng quan") },
  { type: 'divider' as const },
  { key: '/buildings', icon: <BankOutlined />, label: t("Dãy trọ") },
  { key: '/rooms', icon: <HomeOutlined />, label: t("Phòng & người thuê") },
  { key: '/readings', icon: <CalculatorOutlined />, label: t("Lập hóa đơn") },
  { key: '/invoices', icon: <FileTextOutlined />, label: t("Hóa đơn") },
  { key: '/reports', icon: <BarChartOutlined />, label: t("Báo cáo") },
  { type: 'divider' as const },
  { key: 'settings', icon: <SettingOutlined />, label: t("Cấu hình"), children: settingsItems },
  { key: '/account-status', icon: <CrownOutlined />, label: t("Gói cước & tài khoản") },
];
const adminItems = [
  { key: '/admin/owners', icon: <UserOutlined />, label: t("Chủ trọ") },
  { key: '/admin/monitoring', icon: <BarChartOutlined />, label: t("Giám sát") },
];
  const isAdmin = user?.role === 'Admin';
  const items = isAdmin ? adminItems : ownerItems;
  const title = settingsItems.find((item) => item.key === location.pathname)?.label ?? items.find((item) => item.key === location.pathname)?.label ?? 'Roomie';

  const userMenu: MenuProps['items'] = [
    { key: 'account', label: <div className="user-menu-info"><strong>{user?.name}</strong><span>{user?.email}</span></div>, disabled: true },
    { type: 'divider' },
    ...(!isAdmin ? [{ key: 'subscription', icon: <CrownOutlined />, label: t("Gói cước & tài khoản") }] : []),
    { key: 'password', label: t("Đổi mật khẩu") },
    { key: 'logout', icon: <LogoutOutlined />, label: t("Đăng xuất") },
  ];
  const handleMenu = ({ key }: { key: string }) => {
    if (key === 'subscription') navigate('/account-status');
    if (key === 'password') setPasswordOpen(true);
    if (key === 'logout') void signOut().then(() => navigate('/login')).catch((error: Error) => message.error(error.message));
  };
  const menu = <Menu theme="dark" mode="inline" selectedKeys={[location.pathname]} openKeys={openKeys} onOpenChange={setOpenKeys} items={items} onClick={({ key }) => { navigate(key); setMobileOpen(false); }} />;

  return <Layout className="app-layout">
    <ChangePasswordModal open={passwordOpen} onClose={() => setPasswordOpen(false)} />
    <Sider width={250} className="app-sider" breakpoint="lg" collapsedWidth={0} trigger={null} onBreakpoint={(broken) => { if (!broken) setMobileOpen(false); }}>
      <div className="brand"><div className="brand-mark"><RoomieMark /></div><div><strong>Roomie</strong><span>{t("QUẢN LÝ PHÒNG TRỌ")}</span></div></div>
      <div className="sider-label">{t("MENU CHÍNH")}</div>
      {menu}
      <div className="sider-bottom"><div className="help-card"><div className="help-icon">✦</div><strong>{t("Cần hỗ trợ?")}</strong><span>{t("Quản lý nhà trọ dễ dàng, từng bước một.")}</span></div><div className="sider-foot">Roomie <span>•</span>  {t("Phiên bản 1.0")}</div></div>
    </Sider>
    <Drawer className="mobile-menu-drawer" placement="left" width={280} open={mobileOpen} onClose={() => setMobileOpen(false)} title={t("Menu chính")} closable>
      <div className="brand"><div className="brand-mark"><RoomieMark /></div><div><strong>Roomie</strong><span>{t("QUẢN LÝ PHÒNG TRỌ")}</span></div></div><div className="sider-label">{t("MENU CHÍNH")}</div>{menu}
    </Drawer>
    <Layout className="main-layout">
      <Header className="topbar"><div className="topbar-left"><Button className="mobile-menu-button" type="text" icon={<MenuOutlined />} aria-label={t("Mở menu")} onClick={() => setMobileOpen(true)}>{t("Menu")}</Button><div className="breadcrumb-label">{t("Khu vực làm việc")} <span>/</span> <strong>{title}</strong></div></div><Space size={12}><LanguageSelector /><Dropdown menu={{ items: userMenu, onClick: handleMenu }} trigger={['click']} placement="bottomRight"><Button className="profile-button" type="text"><Avatar size={36} className="profile-avatar">{user?.name?.trim()?.charAt(0)?.toUpperCase() || 'N'}</Avatar><span className="profile-text"><strong>{user?.name}</strong><small>{isAdmin ? t("Quản trị viên") : t("Chủ trọ")}</small></span><Typography.Text type="secondary">⌄</Typography.Text></Button></Dropdown></Space></Header>
      <nav className="compact-navigation" aria-label={t("Điều hướng chính")}>{items.filter((item) => item.key).map((item) => item.key === 'settings'
        ? <Dropdown key={item.key} trigger={['click']} menu={{ items: settingsItems, selectedKeys: [location.pathname], onClick: ({ key }) => navigate(key) }}>
          <button type="button" aria-current={location.pathname.startsWith('/settings/') ? 'page' : undefined}>{item.icon}<span>{item.label} <DownOutlined /></span></button>
        </Dropdown>
        : <Link key={item.key} to={item.key!} aria-current={location.pathname === item.key ? 'page' : undefined}>{item.icon}<span>{item.label}</span></Link>)}</nav>
      <Content className="page-content"><div className="page-content-inner">{children}</div></Content>
    </Layout>
  </Layout>;
}
