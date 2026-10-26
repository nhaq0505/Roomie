import { useState } from 'react';
import { Input, Select, Space, Tooltip } from 'antd';
import type { InputProps } from 'antd';
import { t } from '../i18n/runtime';

export interface CountryDialOption {
  dial: string;
  iso: string;
  flag: string;
  label: string;
  shortLabel: string;
}

export const COUNTRY_DIAL_OPTIONS: readonly CountryDialOption[] = [
  { dial: '+84', iso: 'VN', flag: '🇻🇳', label: '🇻🇳 +84 (VN · Zalo)', shortLabel: '+84' },
  { dial: '+82', iso: 'KR', flag: '🇰🇷', label: '🇰🇷 +82 (KR · WhatsApp)', shortLabel: '+82' },
  { dial: '+81', iso: 'JP', flag: '🇯🇵', label: '🇯🇵 +81 (JP · WhatsApp)', shortLabel: '+81' },
  { dial: '+86', iso: 'CN', flag: '🇨🇳', label: '🇨🇳 +86 (CN · WhatsApp)', shortLabel: '+86' },
  { dial: '+886', iso: 'TW', flag: '🇹🇼', label: '🇹🇼 +886 (TW · WhatsApp)', shortLabel: '+886' },
  { dial: '+852', iso: 'HK', flag: '🇭🇰', label: '🇭🇰 +852 (HK · WhatsApp)', shortLabel: '+852' },
  { dial: '+65', iso: 'SG', flag: '🇸🇬', label: '🇸🇬 +65 (SG · WhatsApp)', shortLabel: '+65' },
  { dial: '+66', iso: 'TH', flag: '🇹🇭', label: '🇹🇭 +66 (TH · WhatsApp)', shortLabel: '+66' },
  { dial: '+60', iso: 'MY', flag: '🇲🇾', label: '🇲🇾 +60 (MY · WhatsApp)', shortLabel: '+60' },
  { dial: '+63', iso: 'PH', flag: '🇵🇭', label: '🇵🇭 +63 (PH · WhatsApp)', shortLabel: '+63' },
  { dial: '+62', iso: 'ID', flag: '🇮🇩', label: '🇮🇩 +62 (ID · WhatsApp)', shortLabel: '+62' },
  { dial: '+91', iso: 'IN', flag: '🇮🇳', label: '🇮🇳 +91 (IN · WhatsApp)', shortLabel: '+91' },
  { dial: '+1', iso: 'US', flag: '🇺🇸', label: '🇺🇸 +1 (US/CA · WhatsApp)', shortLabel: '+1' },
  { dial: '+44', iso: 'GB', flag: '🇬🇧', label: '🇬🇧 +44 (UK · WhatsApp)', shortLabel: '+44' },
  { dial: '+61', iso: 'AU', flag: '🇦🇺', label: '🇦🇺 +61 (AU · WhatsApp)', shortLabel: '+61' },
  { dial: '+33', iso: 'FR', flag: '🇫🇷', label: '🇫🇷 +33 (FR · WhatsApp)', shortLabel: '+33' },
  { dial: '+49', iso: 'DE', flag: '🇩🇪', label: '🇩🇪 +49 (DE · WhatsApp)', shortLabel: '+49' },
  { dial: '+7', iso: 'RU', flag: '🇷🇺', label: '🇷🇺 +7 (RU · WhatsApp)', shortLabel: '+7' },
];

const SORTED_DIALS = [...COUNTRY_DIAL_OPTIONS].sort((a, b) => b.dial.length - a.dial.length);

export function parsePhoneValue(raw?: string | null): { country: string; local: string } {
  const trimmed = (raw ?? '').trim();
  if (!trimmed) return { country: '+84', local: '' };

  const normalized = trimmed.startsWith('00') ? `+${trimmed.slice(2)}` : trimmed;
  if (normalized.startsWith('+')) {
    const compact = `+${normalized.slice(1).replace(/\D/g, '')}`;
    for (const option of SORTED_DIALS) {
      if (compact.startsWith(option.dial)) {
        const rest = compact.slice(option.dial.length);
        if (option.dial === '+84') {
          const cleanRest = rest.replace(/^0+/, '');
          return { country: '+84', local: cleanRest ? `0${cleanRest}` : '' };
        }
        return { country: option.dial, local: rest };
      }
    }
  }

  return { country: '+84', local: trimmed.replace(/[^\d]/g, '') };
}

export function formatPhoneOutput(country: string, localInput: string): string {
  const digits = localInput.replace(/\D/g, '');
  if (!digits) return '';
  if (country === '+84') {
    if (digits.startsWith('84') && digits.length >= 10) {
      return `0${digits.slice(2)}`;
    }
    return digits;
  }
  const withoutLeadingZero = digits.replace(/^0+/, '');
  return withoutLeadingZero ? `${country}${withoutLeadingZero}` : '';
}

export interface PhoneInputProps extends Omit<InputProps, 'value' | 'onChange'> {
  value?: string;
  onChange?: (value: string) => void;
  showChannelHint?: boolean;
}

export default function PhoneInput({
  value,
  onChange,
  showChannelHint = true,
  placeholder,
  disabled,
  size,
  id,
  ...rest
}: PhoneInputProps) {
  const parsed = parsePhoneValue(value);
  const [selectedCountry, setSelectedCountry] = useState<string>(parsed.country);

  const activeCountry = value && value.trim().startsWith('+') ? parsed.country : selectedCountry;
  const isVietnam = activeCountry === '+84';

  const handleCountryChange = (nextCountry: string) => {
    setSelectedCountry(nextCountry);
    if (parsed.local) {
      const nextLocal = nextCountry === '+84' && !parsed.local.startsWith('0')
        ? `0${parsed.local}`
        : parsed.local;
      onChange?.(formatPhoneOutput(nextCountry, nextLocal));
    }
  };

  const handleInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const raw = e.target.value;
    if (raw.trim().startsWith('+')) {
      const detected = parsePhoneValue(raw);
      setSelectedCountry(detected.country);
      onChange?.(formatPhoneOutput(detected.country, detected.local));
      return;
    }
    onChange?.(formatPhoneOutput(activeCountry, raw));
  };

  const channelSuffix = showChannelHint ? (
    <Tooltip
      title={
        isVietnam
          ? t("Số Việt Nam (+84): Chỉ gửi nhắc hóa đơn qua Zalo.")
          : t("Số quốc tế ({0}): Mặc định gửi nhắc hóa đơn qua WhatsApp.", [activeCountry])
      }
    >
      <span className={`roomie-phone-badge ${isVietnam ? 'is-zalo' : 'is-whatsapp'}`}>
        {isVietnam ? 'Zalo' : 'WhatsApp'}
      </span>
    </Tooltip>
  ) : undefined;

  return (
    <div className="roomie-phone-field">
      {channelSuffix}
      <Space.Compact className="roomie-phone-input" style={{ width: '100%' }}>
        <Select
          value={activeCountry}
          onChange={handleCountryChange}
          disabled={disabled}
          size={size}
          popupMatchSelectWidth={210}
          className="roomie-phone-country-select"
          aria-label={t("Mã quốc gia")}
          options={COUNTRY_DIAL_OPTIONS.map((item) => ({
            value: item.dial,
            label: item.shortLabel,
            fullLabel: item.label,
          }))}
          optionRender={(option) => option.data.fullLabel}
        />
        <Input
          {...rest}
          id={id}
          size={size}
          disabled={disabled}
          value={parsed.local}
          onChange={handleInputChange}
          placeholder={placeholder ?? (isVietnam ? '0901234567' : '1012345678')}
          inputMode="tel"
        />
      </Space.Compact>
    </div>
  );
}
