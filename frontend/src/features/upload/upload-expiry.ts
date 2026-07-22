export const EXPIRY_OPTIONS = [
  { label: '1d', hours: 24 },
  { label: '7d', hours: 24 * 7 },
  { label: '30d', hours: 24 * 30 }
] as const;

export const DEFAULT_EXPIRY_INDEX = 1;

export function cycleExpiryIndex(index: number): number {
  return (index + 1) % EXPIRY_OPTIONS.length;
}
