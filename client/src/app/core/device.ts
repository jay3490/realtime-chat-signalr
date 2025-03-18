const DEVICE_KEY = 'realtime-chat.device';

let cached: string | null = null;

/**
 * A stable id for this browser, sent with the hub connection so the server can record
 * delivery receipts per device. Kept in local storage; if storage is unavailable the id
 * lasts until the page is closed.
 */
export function deviceId(): string {
  if (cached) {
    return cached;
  }
  let id: string | null = null;
  try {
    id = localStorage.getItem(DEVICE_KEY);
  } catch {
    // Storage blocked: fall through to a fresh id.
  }
  if (!id || !/^[A-Za-z0-9_-]{1,64}$/.test(id)) {
    id = crypto.randomUUID();
    try {
      localStorage.setItem(DEVICE_KEY, id);
    } catch {
      // See above.
    }
  }
  cached = id;
  return id;
}
