import { InputNumber } from 'antd';
import type { InputNumberProps } from 'antd';

type MoneyInputProps = Omit<InputNumberProps, 'formatter' | 'parser' | 'stringMode'>;

function groupThousands(value: unknown) {
  const input = String(value ?? '').replace(/,/g, '');
  if (!input) return '';
  // Leave partial or invalid input untouched so formatting never silently
  // changes the amount the user entered. Ant InputNumber handles precision on blur.
  const match = input.match(/^([+-]?)(\d*)(\.\d*)?$/);
  if (!match) return input;
  const [, sign, integer, fraction = ''] = match;
  const grouped = integer.replace(/\B(?=(\d{3})+(?!\d))/g, ',');
  return `${sign}${grouped}${fraction}`;
}

function parseMoneyInput(value: string | undefined) {
  return (value ?? '').replace(/,/g, '');
}

/** VND input that groups thousands with commas while keeping Ant Form values numeric. */
export default function MoneyInput(props: MoneyInputProps) {
  return <InputNumber
    {...props}
    precision={0}
    formatter={(value, info) => groupThousands(info.userTyping ? info.input : value)}
    parser={parseMoneyInput}
  />;
}
