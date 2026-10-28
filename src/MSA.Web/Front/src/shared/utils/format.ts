export function formatMoney(value: number, currency = 'CNY'): string {
  return new Intl.NumberFormat('zh-CN', {
    style: 'currency',
    currency,
    minimumFractionDigits: 2,
  }).format(value);
}

export function formatDateTime(value: string): string {
  return new Intl.DateTimeFormat('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).format(new Date(value));
}

export function formatPoints(value: number): string {
  return new Intl.NumberFormat('zh-CN').format(value);
}

const relativeTime = new Intl.RelativeTimeFormat('zh-CN', { numeric: 'auto' });

/** "刚刚" / "5 分钟前" / "3 天前"; older than a week falls back to the full date. */
export function formatRelativeTime(value: string, now = Date.now()): string {
  const seconds = Math.round((Date.parse(value) - now) / 1000);
  if (Math.abs(seconds) < 60) return '刚刚';
  const minutes = Math.round(seconds / 60);
  if (Math.abs(minutes) < 60) return relativeTime.format(minutes, 'minute');
  const hours = Math.round(minutes / 60);
  if (Math.abs(hours) < 24) return relativeTime.format(hours, 'hour');
  const days = Math.round(hours / 24);
  if (Math.abs(days) < 7) return relativeTime.format(days, 'day');
  return formatDateTime(value);
}
