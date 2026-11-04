// Link options. Guests have no pickers: they always get the shortest lifetime
// and exactly one access, matching the backend limits.

export const EXPIRY_OPTIONS = [
  { hours: 6, labelKey: 'options.expiry.sixHours' },
  { hours: 24, labelKey: 'options.expiry.oneDay' },
  { hours: 168, labelKey: 'options.expiry.sevenDays' },
  { hours: 720, labelKey: 'options.expiry.thirtyDays' }
] as const;

export const DEFAULT_EXPIRY_INDEX = 2;
export const ANONYMOUS_EXPIRY_INDEX = 0;

export const DOWNLOAD_COUNT_OPTIONS = [1, 3, 5, 10, null] as const;

export const DEFAULT_DOWNLOAD_COUNT_INDEX = 0;
export const ANONYMOUS_DOWNLOAD_COUNT = 1;
