import { CrownOutlined } from '@ant-design/icons';
import { Button } from 'antd';
import { Link } from 'react-router-dom';
import { t } from '../i18n/runtime';

/** Explains a Pro-only capability and links to the plan page; never blocks access to data already recorded. */
export default function ProUpsell({ title, description, compact = false }: { title: string; description: string; compact?: boolean }) {
  return <div className={`pro-upsell ${compact ? 'pro-upsell-compact' : ''}`} role="note">
    <span className="pro-upsell-icon" aria-hidden="true"><CrownOutlined /></span>
    <div className="pro-upsell-copy"><strong>{title}</strong><span>{description}</span></div>
    <Link to="/account-status"><Button type="primary">{t("Nâng cấp Pro")}</Button></Link>
  </div>;
}
