import { useState } from 'react';
import { Input, Select, Tag, Typography } from 'antd';
import type { InputProps } from 'antd';
import { t } from '../i18n/runtime';

export interface CountryDialOption {
  dial: string;
  iso: string;
  flag: string;
  label: string;
}

export const COUNTRY_DIAL_OPTIONS: readonly CountryDialOption[] = [
  { dial: '+84', iso: 'VN', flag: '🇻🇳', label: '🇻🇳 +84 (VN)' },
  { dial: '+82', iso: 'KR', flag: '🇰🇷', label: '🇰🇷 +82 (KR)' },
  { dial: '+81', iso: 'JP', flag: '🇯🇵', label: '🇯🇵 +81 (JP)' },
  { dial: '+86', iso: 'CN', flag: '🇨🇳', label: '🇨🇳 +86 (CN)' },
  { dial: '+886', iso: 'TW', flag: '🇹🇼', label: '🇹🇼 +886 (TW)' },
  { dial: '+852', iso: 'HK', flag: '🇭🇰', label: '🇭🇰 +852 (HK)' },
  { dial: '+65', iso: 'SG', flag: '🇸🇬', label: '🇸🇬 +65 (SG)' },
  { dial: '+66', iso: 'TH', flag: '🇹🇭', label: '🇹🇭 +66 (TH)' },
  { dial: '+60', iso: 'MY', flag: '🇲🇾', label: '🇲🇾 +60 (MY)' },
  { dial: '+63', iso: 'PH', flag: '🇵🇭', label: '🇵🇭 +63 (PH)' },
  { dial: '+62', iso: 'ID', flag: '🇮🇩', label: '🇮🇩 +62 (ID)' },
  { dial: '+91', iso: 'IN', flag: '🇮🇳', label: '🇮🇳 +91 (IN)' },
  { dial: '+1', iso: 'US', flag: '🇺🇸', label: '🇺🇸 +1 (US/CA)' },
  { dial: '+44', iso: 'GB', flag: '🇬🇧', label: '🇬🇧 +44 (UK)' },
  { dial: '+61', iso: 'AU', flag: '🇦🇺', label: '🇦🇺 +61 (AU)' },
  { dial: '+33', iso: 'FR', flag: '🇫🇷', label: '🇫🇷 +33 (FR)' },
  { dial: '+49', iso: 'DE', flag: '🇩🇪', label: '🇩🇪 +49 (DE)' },
  { dial: '+7', iso: 'RU', flag: '🇷🇺', label: '🇷🇺 +7 (RU)' },
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

  // When value has a non-empty international prefix, synchronize country from value;
  // when empty, keep user's selected country so switching country before typing works.
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

  const countrySelector = (
    <Select
      value={activeCountry}
      onChange={handleCountryChange}
      disabled={disabled}
      size={size}
      popupMatchSelectWidth={false}
      style={{ minWidth: 118 }}
      aria-label={t("Mã quốc gia")}
      options={COUNTRY_DIAL_OPTIONS.map((item) => ({
        value: item.dial,
        label: item.label,
      }))}
    />
  );

  return (
    <div className="roomie-phone-input">
      <Input
        {...rest}
        id={id}
        size={size}
        disabled={disabled}
        addonBefore={countrySelector}
        value={parsed.local}
        onChange={handleInputChange}
        placeholder={placeholder ?? (isVietnam ? '0901234567' : '1012345678')}
        inputMode="tel"
      />
      {showChannelHint && (
        <div style={{ marginTop: 4, display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
          <Tag color={isVietnam ? 'blue' : 'green'} style={{ marginInlineEnd: 0 }}>
            {isVietnam ? 'Zalo' : 'WhatsApp'}
          </Tag>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {isVietnam
              ? t("Số Việt Nam (+84): Chỉ gửi nhắc hóa đơn qua Zalo.")
              : t("Số quốc tế ({0}): Mặc định gửi nhắc hóa đơn qua WhatsApp.", [activeCountry])}
          </Typography.Text>
        </div>
      )}
    </div>
  );
}
