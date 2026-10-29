import {
  AimOutlined,
  ArrowRightOutlined,
  BankOutlined,
  BookOutlined,
  CalculatorOutlined,
  CheckCircleFilled,
  CloseOutlined,
  CreditCardOutlined,
  HomeOutlined,
  RocketOutlined,
  UserAddOutlined,
} from '@ant-design/icons';
import { useQuery } from '@tanstack/react-query';
import { Button, Card, Progress, Space, Tag, Typography } from 'antd';
import { useCallback, useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { t } from '../../shared/i18n/runtime';
import { useAuth } from '../auth/session';
import { buildingsApi } from '../buildings/index';
import { invoicesApi } from '../invoices/index';
import { paymentSettingsApi } from '../payments/index';
import { roomsApi } from '../rooms/index';
import { dashboardApi } from './api';

const TOUR_SEEN_STORAGE_PREFIX = 'roomie:onboarding-tour-seen:';
const TOUR_ACTIVE_SESSION_PREFIX = 'roomie:onboarding-tour-active:';
const TOUR_START_EVENT = 'roomie:onboarding-tour-start';

export interface OnboardingChecklistProps {
  userId?: string;
  hasBuilding: boolean;
  hasRoom: boolean;
  hasPayment: boolean;
  hasTenant: boolean;
  hasInvoice: boolean;
  autoOpenTour?: boolean;
  onDismiss: () => void;
}

interface SpotlightRect {
  top: number;
  left: number;
  width: number;
  height: number;
}

function buildSteps(flags: {
  hasBuilding: boolean;
  hasRoom: boolean;
  hasPayment: boolean;
  hasTenant: boolean;
  hasInvoice: boolean;
}) {
  return [
    {
      key: 'building',
      done: flags.hasBuilding,
      icon: <BankOutlined />,
      title: t("Tạo dãy trọ đầu tiên"),
      description: t("Thêm địa điểm cho thuê để gom nhóm các phòng."),
      actionLabel: t("Tạo dãy trọ"),
      to: '/buildings?create=1',
      tourTitle: t("1. Tạo dãy trọ đầu tiên"),
      tourDescription: t("Đặt tên và địa chỉ cho nhà trọ hoặc tòa nhà của bạn để bắt đầu gom nhóm các phòng cho thuê."),
      siderMenuSuffix: '/buildings',
      pageTargetKey: 'building',
    },
    {
      key: 'room',
      done: flags.hasRoom,
      icon: <HomeOutlined />,
      title: t("Tạo phòng trọ"),
      description: t("Thiết lập giá thuê và phí"),
      actionLabel: t("Tạo phòng trọ"),
      to: '/rooms?create=1',
      tourTitle: t("2. Tạo phòng & đơn giá điện/nước"),
      tourDescription: t("Thêm các phòng vào dãy trọ, cài đặt giá thuê tháng, đơn giá điện (đ/kWh), cách tính tiền nước (theo phòng, theo đầu người hoặc theo số đồng hồ m³) và phí dịch vụ."),
      siderMenuSuffix: '/rooms',
      pageTargetKey: 'room',
    },
    {
      key: 'payment',
      done: flags.hasPayment,
      icon: <CreditCardOutlined />,
      title: t("Cài đặt tài khoản nhận tiền VietQR"),
      description: t("Chọn ngân hàng và nhập số tài khoản để tự tạo mã VietQR trên hóa đơn."),
      actionLabel: t("Cài đặt thanh toán"),
      to: '/settings/payment',
      tourTitle: t("3. Cài đặt tài khoản nhận tiền VietQR"),
      tourDescription: t("Chọn ngân hàng và nhập số tài khoản của bạn. Roomie sẽ tự động tạo mã QR chuyển khoản đúng số tiền trên từng hóa đơn gửi khách."),
      siderMenuSuffix: '/settings/payment',
      pageTargetKey: 'payment',
    },
    {
      key: 'tenant',
      done: flags.hasTenant,
      icon: <UserAddOutlined />,
      title: t("Thêm người thuê & cấp link phòng"),
      description: t("Quản lý người đang thuê và ngày chuyển vào"),
      actionLabel: t("Thêm người thuê"),
      to: '/rooms',
      tourTitle: t("4. Thêm người thuê, Hợp đồng & Link phòng"),
      tourDescription: t("Vào phòng trống để thêm khách thuê, nhập chỉ số điện/nước ban đầu, quét QR CCCD/Passport in Hợp đồng thuê + mẫu CT01 và gửi link xem hóa đơn riêng cho khách."),
      siderMenuSuffix: '/rooms',
      pageTargetKey: 'tenant',
    },
    {
      key: 'invoice',
      done: flags.hasInvoice,
      icon: <CalculatorOutlined />,
      title: t("Chốt chỉ số điện & phát hành hóa đơn"),
      description: t("Kiểm tra chỉ số rồi phát hành hóa đơn cho người thuê"),
      actionLabel: t("Nhập chỉ số"),
      to: '/readings',
      tourTitle: t("5. Chốt điện/nước & lập hóa đơn hàng tháng"),
      tourDescription: t("Hàng tháng bạn chỉ cần bấm vào đây để nhập số điện/nước mới. Hệ thống sẽ tự tính tiền, phát hành hóa đơn và chuẩn bị tin nhắc thanh toán."),
      siderMenuSuffix: '/invoices',
      pageTargetKey: 'invoice',
    },
  ];
}

export default function OnboardingChecklist({
  hasBuilding,
  hasRoom,
  hasPayment,
  hasTenant,
  hasInvoice,
  onDismiss,
}: OnboardingChecklistProps) {
  const navigate = useNavigate();
  const steps = buildSteps({ hasBuilding, hasRoom, hasPayment, hasTenant, hasInvoice });
  const completedCount = steps.filter((s) => s.done).length;
  const percent = Math.round((completedCount / steps.length) * 100);
  const firstIncompleteIndex = steps.findIndex((s) => !s.done);
  const nextStep = firstIncompleteIndex >= 0 ? steps[firstIncompleteIndex] : undefined;

  const triggerStartTour = () => {
    window.dispatchEvent(new CustomEvent(TOUR_START_EVENT));
  };

  return (
    <Card className="onboarding-card" bordered={false} data-testid="onboarding-checklist">
      <div className="onboarding-header">
        <div className="onboarding-header-main">
          <div className="onboarding-badge-row">
            <span className="onboarding-eyebrow">
              <RocketOutlined /> {t("HƯỚNG DẪN BẮT ĐẦU NHANH")}
            </span>
            <Tag color={completedCount === steps.length ? 'success' : 'processing'} className="onboarding-progress-tag">
              {t("Hoàn thành {0}/{1} bước thiết lập", [completedCount, steps.length])}
            </Tag>
          </div>
          <Typography.Title level={4} className="onboarding-title">
            {t("Bắt đầu quản lý nhà trọ với Roomie")}
          </Typography.Title>
          <Typography.Paragraph className="onboarding-subtitle">
            {t("Thực hiện lần lượt 5 bước dưới đây để thiết lập phòng, tạo mã VietQR nhận tiền và gửi hóa đơn cho người thuê.")}
          </Typography.Paragraph>
        </div>
        <Space wrap className="onboarding-header-actions">
          <Button
            type="primary"
            ghost
            size="small"
            icon={<AimOutlined />}
            onClick={triggerStartTour}
            data-testid="onboarding-start-tour"
          >
            {t("Xem hướng dẫn từng bước")}
          </Button>
          <Link to="/huong-dan" className="onboarding-guide-link">
            <BookOutlined /> {t("Xem cẩm nang hướng dẫn")}
          </Link>
          <Button type="text" size="small" onClick={onDismiss} data-testid="onboarding-dismiss">
            {t("Ẩn hướng dẫn")}
          </Button>
        </Space>
      </div>

      <div className="onboarding-progress-bar">
        <Progress percent={percent} showInfo={false} strokeColor="#0f9d83" trailColor="#e5efec" size="small" />
      </div>

      <div className="onboarding-steps" role="list">
        {steps.map((step, index) => {
          const isCurrent = nextStep?.key === step.key;
          return (
            <div
              key={step.key}
              role="listitem"
              data-testid={`onboarding-step-${step.key}`}
              data-done={step.done ? 'true' : 'false'}
              className={`onboarding-step ${step.done ? 'is-done' : ''} ${isCurrent ? 'is-current' : ''}`}
            >
              <div className="onboarding-step-indicator">
                {step.done ? (
                  <CheckCircleFilled className="onboarding-step-check" />
                ) : (
                  <span className="onboarding-step-number">{index + 1}</span>
                )}
              </div>
              <div className="onboarding-step-body">
                <div className="onboarding-step-title-row">
                  <strong>{step.title}</strong>
                  {step.done ? (
                    <span className="onboarding-step-status is-done">{t("Đã hoàn tất")}</span>
                  ) : isCurrent ? (
                    <span className="onboarding-step-status is-current">{t("Bước tiếp theo")}</span>
                  ) : null}
                </div>
                <span>{step.description}</span>
              </div>
              <div className="onboarding-step-action">
                <Button
                  type={isCurrent ? 'primary' : 'default'}
                  size="small"
                  onClick={() => navigate(step.to)}
                >
                  {step.actionLabel} <ArrowRightOutlined />
                </Button>
              </div>
            </div>
          );
        })}
      </div>
    </Card>
  );
}

export function OnboardingTourOverlay({
  onEnsureSettingsOpen,
}: {
  onEnsureSettingsOpen?: () => void;
}) {
  const { user } = useAuth();
  const location = useLocation();
  const isOwner = user?.role === 'Owner' && user?.status === 'Approved';
  const userId = user?.id ?? 'owner';
  const tourSeenKey = `${TOUR_SEEN_STORAGE_PREFIX}${userId}`;
  const tourActiveKey = `${TOUR_ACTIVE_SESSION_PREFIX}${userId}`;

  const dashboardQuery = useQuery({ queryKey: ['dashboard'], queryFn: dashboardApi, enabled: isOwner });
  const roomsQuery = useQuery({ queryKey: ['rooms'], queryFn: () => roomsApi.list(), enabled: isOwner });
  const buildingsQuery = useQuery({ queryKey: ['buildings'], queryFn: () => buildingsApi.list(), enabled: isOwner });
  const paymentSettingsQuery = useQuery({ queryKey: ['payment-settings'], queryFn: paymentSettingsApi.get, enabled: isOwner });
  const summary = dashboardQuery.data;
  const invoicesQuery = useQuery({
    queryKey: ['invoices', summary?.period],
    queryFn: () => invoicesApi.list(summary!.period),
    enabled: isOwner && !!summary,
  });

  const rooms = Array.isArray(roomsQuery.data) ? roomsQuery.data : [];
  const buildings = Array.isArray(buildingsQuery.data) ? buildingsQuery.data : [];
  const invoices = Array.isArray(invoicesQuery.data) ? invoicesQuery.data : [];
  const paymentSettings =
    paymentSettingsQuery.data &&
    typeof paymentSettingsQuery.data === 'object' &&
    !Array.isArray(paymentSettingsQuery.data)
      ? paymentSettingsQuery.data
      : null;

  const hasRoom = (summary?.totalRooms ?? 0) > 0 || rooms.some((r) => !r.isArchived);
  const hasBuilding = buildings.some((b) => !b.isArchived) || hasRoom;
  const hasPayment =
    (paymentSettings?.confirmationMode === 'Automatic'
      ? paymentSettings?.webhookStatus === 'Verified'
      : Boolean(paymentSettings?.manualBank?.bankBin && paymentSettings?.manualBank?.accountNumber)) ||
    rooms.some((r) => Boolean(r.occupancy?.token));
  const hasTenant = (summary?.occupiedRooms ?? 0) > 0 || rooms.some((r) => Boolean(r.occupancy));
  const hasInvoice = (summary?.totalBilled ?? 0) > 0 || (summary?.monthlyBilled ?? 0) > 0 || invoices.some((inv) => inv.kind !== 'Deposit');

  const steps = buildSteps({ hasBuilding, hasRoom, hasPayment, hasTenant, hasInvoice });
  const completedCount = steps.filter((s) => s.done).length;
  const firstIncompleteIndex = steps.findIndex((s) => !s.done);
  const queriesReady =
    !dashboardQuery.isPending &&
    !roomsQuery.isPending &&
    !buildingsQuery.isPending &&
    !paymentSettingsQuery.isPending;

  const [tourOpen, setTourOpen] = useState<boolean>(() => {
    try {
      return sessionStorage.getItem(tourActiveKey) === '1';
    } catch {
      return false;
    }
  });
  const [tourStepIndex, setTourStepIndex] = useState(0);
  const [spotlightRect, setSpotlightRect] = useState<SpotlightRect | null>(null);
  const [pausedForForm, setPausedForForm] = useState(false);

  const setTourActivePersisted = useCallback(
    (active: boolean) => {
      setTourOpen(active);
      try {
        if (active) {
          sessionStorage.setItem(tourActiveKey, '1');
        } else {
          sessionStorage.removeItem(tourActiveKey);
        }
      } catch {
        /* ignore */
      }
    },
    [tourActiveKey],
  );

  const closeTour = useCallback(() => {
    try {
      localStorage.setItem(tourSeenKey, '1');
    } catch {
      /* ignore */
    }
    setTourActivePersisted(false);
  }, [tourSeenKey, setTourActivePersisted]);

  // Listen for manual "Xem hướng dẫn từng bước" button click
  useEffect(() => {
    const onStartTour = () => {
      const startIdx = firstIncompleteIndex >= 0 ? firstIncompleteIndex : 0;
      setTourStepIndex(startIdx);
      setTourActivePersisted(true);
    };
    window.addEventListener(TOUR_START_EVENT, onStartTour);
    return () => window.removeEventListener(TOUR_START_EVENT, onStartTour);
  }, [firstIncompleteIndex, setTourActivePersisted]);

  // Auto-open on first visit when 0/5 steps completed and real payment settings loaded
  useEffect(() => {
    if (!isOwner || !queriesReady) return;
    if (location.pathname !== '/dashboard') return;
    if (!paymentSettings?.confirmationMode) return;
    if (completedCount > 0) return;
    try {
      if (localStorage.getItem(tourSeenKey) === '1') return;
    } catch {
      return;
    }
    setTourStepIndex(0);
    setTourActivePersisted(true);
  }, [isOwner, queriesReady, location.pathname, paymentSettings?.confirmationMode, completedCount, tourSeenKey, setTourActivePersisted]);

  // Automatically advance to the next incomplete step when the current step becomes completed!
  const prevDoneRef = useRef<boolean[]>(steps.map((s) => s.done));
  useEffect(() => {
    const prev = prevDoneRef.current;
    const currentDone = steps.map((s) => s.done);
    prevDoneRef.current = currentDone;

    if (!tourOpen || !queriesReady) return;

    if (firstIncompleteIndex < 0) {
      closeTour();
      return;
    }

    const newlyCompletedIdx = currentDone.findIndex((done, idx) => done && !prev[idx]);
    if (newlyCompletedIdx >= 0 || currentDone[tourStepIndex]) {
      if (tourStepIndex !== firstIncompleteIndex) {
        setTourStepIndex(firstIncompleteIndex);
      }
    }
  }, [steps, tourOpen, queriesReady, firstIncompleteIndex, tourStepIndex, closeTour]);

  const activeTourStep = steps[tourStepIndex] ?? steps[0];

  // Expand "Cấu hình" submenu on left sidebar when on Step 3 (payment)
  useEffect(() => {
    if (tourOpen && activeTourStep.key === 'payment') {
      onEnsureSettingsOpen?.();
    }
  }, [tourOpen, activeTourStep.key, onEnsureSettingsOpen]);

  // When user reaches /readings on Step 5, complete the tour
  useEffect(() => {
    if (tourOpen && activeTourStep.key === 'invoice' && location.pathname === '/readings') {
      closeTour();
    }
  }, [tourOpen, activeTourStep.key, location.pathname, closeTour]);

  useEffect(() => {
    if (!tourOpen || !isOwner) {
      setSpotlightRect(null);
      setPausedForForm(false);
      return;
    }

    let highlightedEl: HTMLElement | null = null;

    const findVisibleElement = (selector: string): HTMLElement | null => {
      const nodes = document.querySelectorAll<HTMLElement>(selector);
      for (const el of nodes) {
        const rect = el.getBoundingClientRect();
        if (rect.width > 0 && rect.height > 0) {
          return el;
        }
      }
      return null;
    };

    const isAnyModalOpen = (): boolean => {
      const modals = document.querySelectorAll<HTMLElement>('.ant-modal-wrap, .ant-drawer-content-wrapper');
      for (const m of modals) {
        const style = window.getComputedStyle(m);
        if (style.display !== 'none' && style.visibility !== 'hidden') {
          const rect = m.getBoundingClientRect();
          if (rect.width > 0 && rect.height > 0) return true;
        }
      }
      return false;
    };

    const resolveTargetElement = (): { el: HTMLElement | null; pause: boolean } => {
      const path = location.pathname;
      const stepKey = activeTourStep.key;

      // Pause overlay while user is filling out a modal/drawer
      if (isAnyModalOpen()) {
        return { el: null, pause: true };
      }

      // Pause overlay while user is filling inline forms on dedicated form pages
      if (stepKey === 'payment' && path === '/settings/payment') {
        return { el: null, pause: true };
      }
      if (path.startsWith('/rooms/') && (path.endsWith('/tenant') || path.endsWith('/documents'))) {
        return { el: null, pause: true };
      }
      if (stepKey === 'invoice' && path === '/readings') {
        return { el: null, pause: true };
      }

      // If user is already on the destination page for this step, highlight the page action button
      if (stepKey === 'building' && path === '/buildings') {
        const pageBtn = findVisibleElement('[data-tour-page-target="building"]');
        if (pageBtn) return { el: pageBtn, pause: false };
      }
      if (stepKey === 'room' && path === '/rooms') {
        const pageBtn = findVisibleElement('[data-tour-page-target="room"]');
        if (pageBtn) return { el: pageBtn, pause: false };
      }
      if (stepKey === 'tenant' && path === '/rooms') {
        const pageBtn = findVisibleElement('[data-tour-page-target="tenant"]');
        if (pageBtn) return { el: pageBtn, pause: false };
      }
      if (stepKey === 'invoice' && path === '/invoices') {
        const pageBtn = findVisibleElement('[data-tour-page-target="invoice"]');
        if (pageBtn) return { el: pageBtn, pause: false };
      }

      // Otherwise, highlight the corresponding navigation item on the left sidebar (.app-sider)
      const siderItem = findVisibleElement(`.app-sider [data-menu-id$="${activeTourStep.siderMenuSuffix}"]`);
      if (siderItem) {
        return { el: siderItem, pause: false };
      }
      if (stepKey === 'payment') {
        const settingsSubmenu = findVisibleElement('.app-sider [data-menu-id$="settings"]');
        if (settingsSubmenu) {
          return { el: settingsSubmenu, pause: false };
        }
      }

      const fallbackTarget = findVisibleElement(`[data-tour-target="${stepKey}"]`);
      if (fallbackTarget) {
        return { el: fallbackTarget, pause: false };
      }

      const compactNavLink = findVisibleElement(`.compact-navigation a[href="${activeTourStep.siderMenuSuffix}"]`);
      if (compactNavLink) {
        return { el: compactNavLink, pause: false };
      }
      if (stepKey === 'room' && path === '/buildings') {
        const viewRoomsLink = findVisibleElement('.building-view-rooms');
        if (viewRoomsLink) {
          return { el: viewRoomsLink, pause: false };
        }
      }
      const mobileMenuBtn = findVisibleElement('.mobile-menu-button');
      return { el: mobileMenuBtn, pause: false };
    };

    const updateSpotlight = () => {
      const { el, pause } = resolveTargetElement();
      setPausedForForm(pause);

      if (highlightedEl && highlightedEl !== el) {
        highlightedEl.classList.remove('onboarding-tour-active-target');
      }
      highlightedEl = el;

      if (!el || pause) {
        setSpotlightRect(null);
        return;
      }

      el.classList.add('onboarding-tour-active-target');
      const rect = el.getBoundingClientRect();
      setSpotlightRect({
        top: rect.top,
        left: rect.left,
        width: rect.width,
        height: rect.height,
      });
    };

    updateSpotlight();
    const timer0 = window.setTimeout(updateSpotlight, 30);
    const timer1 = window.setTimeout(updateSpotlight, 90);
    const timer2 = window.setTimeout(updateSpotlight, 180);
    const timer3 = window.setTimeout(updateSpotlight, 380);
    const interval = window.setInterval(updateSpotlight, 350);

    window.addEventListener('resize', updateSpotlight);
    window.addEventListener('scroll', updateSpotlight, true);

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !isAnyModalOpen()) {
        closeTour();
      }
    };
    window.addEventListener('keydown', handleKeyDown);

    return () => {
      window.clearTimeout(timer0);
      window.clearTimeout(timer1);
      window.clearTimeout(timer2);
      window.clearTimeout(timer3);
      window.clearInterval(interval);
      window.removeEventListener('resize', updateSpotlight);
      window.removeEventListener('scroll', updateSpotlight, true);
      window.removeEventListener('keydown', handleKeyDown);
      if (highlightedEl) {
        highlightedEl.classList.remove('onboarding-tour-active-target');
      }
    };
  }, [tourOpen, isOwner, location.pathname, activeTourStep.key, activeTourStep.siderMenuSuffix, closeTour]);

  if (!tourOpen || !isOwner || pausedForForm || typeof document === 'undefined') {
    return null;
  }

  const computePopoverStyle = (): React.CSSProperties => {
    if (typeof window === 'undefined' || !spotlightRect) {
      return {
        top: '50%',
        left: '50%',
        transform: 'translate(-50%, -50%)',
      };
    }
    const vw = window.innerWidth;
    const vh = window.innerHeight;
    const cardWidth = Math.min(360, vw - 24);
    const estimatedHeight = 165;
    const pad = 16;

    // If highlighting an item in the left sidebar (.app-sider), place popover to the RIGHT of the sidebar item
    if (spotlightRect.left < 250 && vw > 768) {
      const left = Math.min(vw - cardWidth - 16, spotlightRect.left + spotlightRect.width + pad);
      const top = Math.max(16, Math.min(vh - estimatedHeight - 16, spotlightRect.top - 8));
      return {
        top: `${Math.round(top)}px`,
        left: `${Math.round(left)}px`,
        width: `${cardWidth}px`,
      };
    }

    let left = spotlightRect.left + spotlightRect.width / 2 - cardWidth / 2;
    left = Math.max(12, Math.min(left, vw - cardWidth - 12));

    const spaceBelow = vh - (spotlightRect.top + spotlightRect.height);
    const placeAbove = spaceBelow < estimatedHeight + 24 && spotlightRect.top > estimatedHeight + 24;

    const top = placeAbove
      ? Math.max(12, spotlightRect.top - estimatedHeight - pad)
      : Math.min(vh - estimatedHeight - 12, spotlightRect.top + spotlightRect.height + pad);

    return {
      top: `${Math.round(top)}px`,
      left: `${Math.round(left)}px`,
      width: `${cardWidth}px`,
    };
  };

  const cutout = spotlightRect
    ? {
        top: Math.max(4, spotlightRect.top - 6),
        left: Math.max(4, spotlightRect.left - 6),
        width: spotlightRect.width + 12,
        height: spotlightRect.height + 12,
      }
    : null;

  return createPortal(
    <div
      className="onboarding-tour-root"
      data-testid="onboarding-spotlight-tour"
      role="dialog"
      aria-modal="true"
      aria-label={activeTourStep.tourTitle}
    >
      {cutout ? (
        <>
          <div className="onboarding-tour-shield" style={{ top: 0, left: 0, right: 0, height: `${cutout.top}px` }} />
          <div
            className="onboarding-tour-shield"
            style={{ top: `${cutout.top + cutout.height}px`, left: 0, right: 0, bottom: 0 }}
          />
          <div
            className="onboarding-tour-shield"
            style={{ top: `${cutout.top}px`, left: 0, width: `${cutout.left}px`, height: `${cutout.height}px` }}
          />
          <div
            className="onboarding-tour-shield"
            style={{
              top: `${cutout.top}px`,
              left: `${cutout.left + cutout.width}px`,
              right: 0,
              height: `${cutout.height}px`,
            }}
          />
          <div
            className="onboarding-tour-cutout"
            style={{
              top: `${cutout.top}px`,
              left: `${cutout.left}px`,
              width: `${cutout.width}px`,
              height: `${cutout.height}px`,
            }}
          />
        </>
      ) : (
        <div className="onboarding-tour-backdrop-dim" />
      )}

      <div className="onboarding-tour-popover" style={computePopoverStyle()}>
        <div className="onboarding-tour-popover-top">
          <span className="onboarding-tour-step-badge">
            {t("Bước {0}/{1}", [tourStepIndex + 1, steps.length])}
          </span>
          <div className="onboarding-tour-dots" aria-hidden="true">
            {steps.map((s, idx) => (
              <span
                key={s.key}
                className={`onboarding-tour-dot ${idx === tourStepIndex ? 'is-active' : ''} ${s.done ? 'is-done' : ''}`}
              />
            ))}
          </div>
          <button
            type="button"
            className="onboarding-tour-close"
            data-testid="onboarding-tour-close"
            aria-label={t("Bỏ qua hướng dẫn")}
            onClick={closeTour}
          >
            <CloseOutlined />
          </button>
        </div>

        <div className="onboarding-tour-title-row">
          <h4>{activeTourStep.tourTitle}</h4>
          {activeTourStep.done ? (
            <span className="onboarding-step-status is-done">{t("Đã hoàn tất")}</span>
          ) : null}
        </div>

        <p className="onboarding-tour-desc">{activeTourStep.tourDescription}</p>
      </div>
    </div>,
    document.body,
  );
}
