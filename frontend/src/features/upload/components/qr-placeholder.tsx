import { For } from 'solid-js';

// Fixed, purely decorative pattern resembling a QR code — not a real encoding of anything.
const PATTERN = [
  [1, 1, 1, 1, 1, 0, 0, 1, 1, 1],
  [1, 0, 0, 0, 1, 0, 1, 0, 0, 1],
  [1, 0, 1, 0, 1, 0, 0, 0, 1, 1],
  [1, 0, 0, 0, 1, 1, 0, 1, 0, 1],
  [1, 1, 1, 1, 1, 0, 1, 0, 0, 0],
  [0, 0, 1, 0, 0, 1, 0, 1, 1, 0],
  [1, 0, 1, 1, 0, 0, 1, 0, 0, 1],
  [1, 0, 0, 0, 1, 1, 1, 1, 1, 1],
  [1, 0, 1, 0, 0, 0, 1, 0, 0, 1],
  [1, 0, 1, 1, 1, 0, 1, 0, 0, 1]
];

export function QrPlaceholder() {
  return (
    <div
      class="grid h-14 w-14 shrink-0 grid-cols-10 gap-px rounded-md border border-border-soft bg-panel p-1.5"
      aria-hidden="true"
    >
      <For each={PATTERN.flat()}>{(cell) => <div class={cell ? 'bg-ink' : 'bg-transparent'} />}</For>
    </div>
  );
}
