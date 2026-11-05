export type RelativeSpan = {
  past: boolean;
  unit: 'minute' | 'hour' | 'day';
  count: number;
};

const MINUTE_MS = 60_000;
const HOUR_MS = 60 * MINUTE_MS;
const DAY_MS = 24 * HOUR_MS;

const createdAtFormatter = new Intl.DateTimeFormat(undefined, {
  day: 'numeric',
  month: 'short',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23'
});

export const formatCreatedAt = (iso: string): string => {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return iso;
  }
  return createdAtFormatter.format(date);
};

/**
 * Compact relative distance used for both "expires in …" and "expired … ago".
 * Values round away from zero so an almost-expired link never says "0 min".
 */
export const relativeSpan = (iso: string, now = Date.now()): RelativeSpan | null => {
  const target = Date.parse(iso);
  if (Number.isNaN(target)) {
    return null;
  }

  const diff = target - now;
  const past = diff < 0;
  const distance = Math.abs(diff);

  if (distance < HOUR_MS) {
    return { past, unit: 'minute', count: Math.max(1, Math.ceil(distance / MINUTE_MS)) };
  }
  if (distance < DAY_MS) {
    return { past, unit: 'hour', count: Math.ceil(distance / HOUR_MS) };
  }
  return { past, unit: 'day', count: Math.ceil(distance / DAY_MS) };
};

export const shortPackageId = (id: string): string => id.replace(/-/g, '').slice(0, 8);

export const packageLink = (id: string): string => `${window.location.origin}/s/${id}`;
