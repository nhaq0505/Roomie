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
  SettingOutlined, DownOutlined, CrownOutlined, FileTextOutlined, GiftOutlined, HomeOutlined, LogoutOutlined,
  MailOutlined, MenuOutlined, NotificationOutlined, TeamOutlined, UserOutlined, WalletOutlined,
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
  { key: '/expenses', icon: <WalletOutlined />, label: t("Chi phí") },
  { key: '/reports', icon: <BarChartOutlined />, label: t("Báo cáo") },
  { type: 'divider' as const },
  { key: 'settings', icon: <SettingOutlined />, label: t("Cấu hình"), children: settingsItems },
  { key: '/profile', icon: <UserOutlined />, label: t("Hồ sơ") },
  { key: '/account-status', icon: <CrownOutlined />, label: t("Gói cước") },
];
const marketerItems = [
  { key: '/admin/monitoring', icon: <BarChartOutlined />, label: t("Tăng trưởng & Doanh thu") },
  { key: '/admin/promotions', icon: <GiftOutlined />, label: t("Khuyến mãi & Voucher") },
  { key: '/marketer/leads', icon: <TeamOutlined />, label: t("Chăm sóc Chủ trọ (CRM)") },
  { key: '/marketer/content', icon: <NotificationOutlined />, label: t("Cẩm nang & Banner") },
  { key: '/marketer/emails', icon: <MailOutlined />, label: t("Email theo Phân khúc") },
];
const adminItems = [
  { key: '/admin/owners', icon: <UserOutlined />, label: t("Chủ trọ & Nhân sự") },
  { key: '/admin/monitoring', icon: <BarChartOutlined />, label: t("Giám sát") },
  { key: '/admin/promotions', icon: <GiftOutlined />, label: t("Khuyến mãi") },
  { type: 'divider' as const },
  { key: '/marketer/leads', icon: <TeamOutlined />, label: t("Chăm sóc Chủ trọ (CRM)") },
  { key: '/marketer/content', icon: <NotificationOutlined />, label: t("Cẩm nang & Banner") },
  { key: '/marketer/emails', icon: <MailOutlined />, label: t("Email theo Phân khúc") },
];
  const isAdmin = user?.role === 'Admin';
  const isMarketer = user?.role === 'Marketer';
  const isStaff = isAdmin || isMarketer;
  const items = isAdmin ? adminItems : isMarketer ? marketerItems : ownerItems;
  const activeMenuKey = location.pathname.startsWith('/rooms/') ? '/rooms' : location.pathname;
  const primaryMobileKeys = new Set(['/dashboard', '/rooms', '/readings', '/invoices', 'settings']);
  const title = settingsItems.find((item) => item.key === activeMenuKey)?.label ?? items.find((item) => item.key === activeMenuKey)?.label ?? 'Roomie';

  const userMenu: MenuProps['items'] = [
    { key: 'account', label: <div className="user-menu-info"><strong>{user?.name}</strong><span>{user?.email}</span></div>, disabled: true },
    { type: 'divider' },
    ...(!isStaff ? [
      { key: 'profile', icon: <UserOutlined />, label: t("Hồ sơ") },
      { key: 'subscription', icon: <CrownOutlined />, label: t("Gói cước") },
    ] : []),
    { key: 'password', label: t("Đổi mật khẩu") },
    { key: 'logout', icon: <LogoutOutlined />, label: t("Đăng xuất") },
  ];
  const handleMenu = ({ key }: { key: string }) => {
    if (key === 'profile') navigate('/profile');
    if (key === 'subscription') navigate('/account-status');
    if (key === 'password') setPasswordOpen(true);
    if (key === 'logout') void signOut().then(() => navigate('/login')).catch((error: Error) => message.error(error.message));
  };
  const menu = <Menu theme="dark" mode="inline" selectedKeys={[activeMenuKey]} openKeys={openKeys} onOpenChange={setOpenKeys} items={items} onClick={({ key }) => { navigate(key); setMobileOpen(false); }} />;

  return <Layout className="app-layout">
    <ChangePasswordModal open={passwordOpen} onClose={() => setPasswordOpen(false)} />
    <Sider width={250} className="app-sider" breakpoint="lg" collapsedWidth={0} trigger={null} onBreakpoint={(broken) => { if (!broken) setMobileOpen(false); }}>
      <div className="brand"><div className="brand-mark"><RoomieMark /></div><div><strong>Roomie</strong><span>{t("QUẢN LÝ PHÒNG TRỌ")}</span></div></div>
      <div className="sider-label">{t("MENU CHÍNH")}</div>
      {menu}
      <div className="sider-bottom"><Link to="/huong-dan" className="help-card"><div className="help-icon">✦</div><strong>{t("Cần hỗ trợ?")}</strong><span>{t("Quản lý nhà trọ dễ dàng, từng bước một.")}</span></Link><div className="sider-foot">Roomie <span>•</span>  {t("Phiên bản 1.0")}</div></div>
    </Sider>
    <Drawer className="mobile-menu-drawer" placement="left" width={280} open={mobileOpen} onClose={() => setMobileOpen(false)} title={t("Menu chính")} closable>
      <div className="brand"><div className="brand-mark"><RoomieMark /></div><div><strong>Roomie</strong><span>{t("QUẢN LÝ PHÒNG TRỌ")}</span></div></div><div className="sider-label">{t("MENU CHÍNH")}</div>{menu}
      <div className="mobile-drawer-account">
        <div className="mobile-drawer-user">
          <strong>{user?.name}</strong>
          <span>{user?.email}</span>
        </div>
        <div className="mobile-drawer-actions">
          {!isStaff && (
            <>
              <Button size="small" ghost icon={<UserOutlined />} onClick={() => { setMobileOpen(false); navigate('/profile'); }}>{t("Hồ sơ")}</Button>
              <Button size="small" ghost icon={<CrownOutlined />} onClick={() => { setMobileOpen(false); navigate('/account-status'); }}>{t("Gói cước")}</Button>
            </>
          )}
          <Button size="small" ghost onClick={() => { setMobileOpen(false); setPasswordOpen(true); }}>{t("Đổi mật khẩu")}</Button>
          <Button size="small" danger ghost icon={<LogoutOutlined />} onClick={() => { setMobileOpen(false); void signOut().then(() => navigate('/login')).catch((error: Error) => message.error(error.message)); }}>{t("Đăng xuất")}</Button>
        </div>
      </div>
    </Drawer>
    <Layout className="main-layout">
      <Header className="topbar"><div className="topbar-left"><Button className="mobile-menu-button" type="text" icon={<MenuOutlined />} aria-label={t("Mở menu")} onClick={() => setMobileOpen(true)}>{t("Menu")}</Button><div className="breadcrumb-label">{t("Khu vực làm việc")} <span>/</span> <strong>{title}</strong></div></div><Space size={12}><LanguageSelector /><Dropdown menu={{ items: userMenu, onClick: handleMenu }} trigger={['click']} placement="bottomRight"><Button className="profile-button" type="text"><Avatar size={36} className="profile-avatar">{user?.name?.trim()?.charAt(0)?.toUpperCase() || 'N'}</Avatar><span className="profile-text"><strong>{user?.name}</strong><small>{isAdmin ? t("Quản trị viên") : isMarketer ? t("Chuyên viên Marketing") : t("Chủ trọ")}</small></span><Typography.Text type="secondary">⌄</Typography.Text></Button></Dropdown></Space></Header>
      <nav className="compact-navigation" data-role={isStaff ? 'admin' : 'owner'} aria-label={t("Điều hướng chính")}>{items.filter((item) => item.key).map((item) => {
        const isSecondary = !isStaff && !primaryMobileKeys.has(String(item.key));
        if (item.key === 'settings') {
          return (
            <Dropdown key={item.key} overlayClassName="compact-nav-dropdown" trigger={['click']} placement="topRight" menu={{ items: settingsItems, selectedKeys: [location.pathname], onClick: ({ key }) => navigate(key) }}>
              <button type="button" aria-label={String(item.label)} data-mobile-secondary={isSecondary ? 'true' : undefined} aria-current={location.pathname.startsWith('/settings/') ? 'page' : undefined}>{item.icon}<span className="compact-nav-text">{item.label} <DownOutlined className="compact-nav-caret" /></span></button>
            </Dropdown>
          );
        }
        return (
          <Link key={item.key} to={item.key!} aria-label={String(item.label)} data-mobile-secondary={isSecondary ? 'true' : undefined} aria-current={activeMenuKey === item.key ? 'page' : undefined}>
            {item.icon}
            {item.key === '/rooms' ? (
              <span className="compact-nav-text">
                <span className="compact-nav-label-full">{item.label}</span>
                <span className="compact-nav-label-short">{t("Phòng")}</span>
              </span>
            ) : (
              <span className="compact-nav-text">{item.label}</span>
            )}
          </Link>
        );
      })}</nav>
      <Content className="page-content"><div className="page-content-inner">{children}</div></Content>
    </Layout>
  </Layout>;
}
