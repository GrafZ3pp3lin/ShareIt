/**
 * Minimal XMLHttpRequest test double for the `components` Vitest project.
 * happy-dom's real XHR performs actual network requests, which API wrappers
 * and pages must not do in tests — this double queues scripted responses that
 * are consumed in send order and never touch the network.
 */
export type FakeResponse = {
  status: number;
  body?: unknown;
  networkError?: boolean;
};

// DOM listeners arrive with their concrete event types (e.g. ProgressEvent);
// `never` accepts any of them contravariantly while staying type-safe.
type XhrListener = (event: never) => void;

export class FakeXhr {
  static responses: FakeResponse[] = [];
  static loadDelayMs = 10;
  static sent: FakeXhr[] = [];

  static reset() {
    FakeXhr.responses = [];
    FakeXhr.sent = [];
    FakeXhr.loadDelayMs = 10;
  }

  status = 0;
  response: unknown = null;
  responseType = '';

  readonly upload = {
    addEventListener: (name: string, listener: XhrListener) => {
      FakeXhr.#add(this.#uploadListeners, name, listener);
    }
  };

  #url = '';
  #form: FormData | null = null;
  #aborted = false;
  #headers = new Map<string, string>();
  #listeners = new Map<string, XhrListener[]>();
  #uploadListeners = new Map<string, XhrListener[]>();

  static #add(map: Map<string, XhrListener[]>, name: string, listener: XhrListener) {
    const existing = map.get(name) ?? [];
    existing.push(listener);
    map.set(name, existing);
  }

  static #dispatch(map: Map<string, XhrListener[]>, name: string, event: unknown = {}) {
    for (const listener of map.get(name) ?? []) {
      // oxlint-disable-next-line no-unsafe-type-assertion -- listeners are scripted test doubles' consumers
      listener(event as never);
    }
  }

  open(_method: string, url: string) {
    this.#url = url;
  }

  setRequestHeader(name: string, value: string) {
    this.#headers.set(name, value);
  }

  addEventListener(name: string, listener: XhrListener) {
    FakeXhr.#add(this.#listeners, name, listener);
  }

  removeEventListener(name: string, listener: XhrListener) {
    const existing = this.#listeners.get(name);
    if (existing) {
      this.#listeners.set(
        name,
        existing.filter((entry) => entry !== listener)
      );
    }
  }

  send(form: FormData) {
    this.#form = form;
    FakeXhr.sent.push(this);
    setTimeout(() => this.#finish(), FakeXhr.loadDelayMs);
  }

  abort() {
    if (this.#aborted) {
      return;
    }
    this.#aborted = true;
    FakeXhr.#dispatch(this.#listeners, 'abort');
  }

  get url() {
    return this.#url;
  }

  get form() {
    return this.#form;
  }

  get requestHeaders() {
    return Object.fromEntries(this.#headers);
  }

  #finish() {
    if (this.#aborted) {
      return;
    }
    const next = FakeXhr.responses.shift() ?? { status: 200, body: { Id: 'fake-package' } };
    this.status = next.status;
    this.response = next.body;
    if (next.networkError) {
      FakeXhr.#dispatch(this.#listeners, 'error');
      return;
    }
    FakeXhr.#dispatch(this.#uploadListeners, 'progress', { lengthComputable: true, loaded: 6, total: 10 });
    FakeXhr.#dispatch(this.#listeners, 'load');
  }
}
