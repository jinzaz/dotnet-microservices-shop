const countryCodes = [
  ['86', '中国大陆'],
  ['852', '中国香港'],
  ['853', '中国澳门'],
  ['886', '中国台湾'],
  ['1', '美国 / 加拿大'],
  ['65', '新加坡'],
  ['81', '日本'],
  ['82', '韩国'],
] as const;

export const defaultCountryCode = '86';

/** 区号 + 本地号码 → 后端要求的 E.164；不合法时返回 null。 */
export function toE164(countryCode: string, nationalNumber: string): string | null {
  const digits = nationalNumber.replace(/[\s-]/g, '');
  if (countryCode === '86' ? !/^1[0-9]{10}$/.test(digits) : !/^[0-9]{4,14}$/.test(digits)) return null;
  const phone = `+${countryCode}${digits}`;
  return /^\+[1-9][0-9]{7,14}$/.test(phone) ? phone : null;
}

/** E.164 → 区号 + 本地号码，按最长区号匹配；认不出的区号原样放进号码框。 */
export function splitE164(phone: string | null | undefined): [countryCode: string, nationalNumber: string] {
  if (!phone) return [defaultCountryCode, ''];
  const match = countryCodes
    .map(([code]) => code)
    .filter((code) => phone.startsWith(`+${code}`))
    .sort((a, b) => b.length - a.length)[0];
  return match ? [match, phone.slice(match.length + 1)] : [defaultCountryCode, phone];
}

type CountryCodeSelectProps = {
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
};

/** 放在 FormField 的 leading 里：收起时只显示 +86，展开是原生下拉（键盘、读屏都可用）。 */
export function CountryCodeSelect({ value, onChange, disabled }: CountryCodeSelectProps) {
  return (
    <span className="country-code">
      <span aria-hidden="true">+{value}</span>
      <span className="country-code__caret" aria-hidden="true">▾</span>
      <select aria-label="国家或地区区号" value={value} disabled={disabled} onChange={(event) => onChange(event.target.value)}>
        {countryCodes.map(([code, name]) => <option key={`${code}-${name}`} value={code}>{name} +{code}</option>)}
      </select>
    </span>
  );
}
