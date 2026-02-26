const GUEST_KEY = 'shareit-guest-id';

export function getGuestId() {
  if (typeof window === 'undefined') {
    return undefined;
  }

  const existing = window.localStorage.getItem(GUEST_KEY);
  if (existing) {
    return existing;
  }

  const id = crypto.randomUUID();
  window.localStorage.setItem(GUEST_KEY, id);
  return id;
}
