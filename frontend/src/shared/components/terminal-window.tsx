import type { JSX } from 'solid-js';

type TerminalWindowProps = {
  label: string;
  children: JSX.Element;
};

export function TerminalWindow(props: TerminalWindowProps) {
  return (
    <div class="overflow-hidden rounded-2xl border border-border-soft bg-panel shadow-card">
      <div class="flex items-center gap-2 border-b border-border-soft bg-panel-soft/50 px-4 py-3 font-mono text-xs text-muted">
        <div class="flex gap-1.5">
          <div class="terminal-dot" />
          <div class="terminal-dot" />
          <div class="terminal-dot" />
        </div>
        <span>{props.label}</span>
      </div>
      <div class="p-5 sm:p-6">{props.children}</div>
    </div>
  );
}
