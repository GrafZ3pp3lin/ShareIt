import type { UploadMode } from './upload-types';

type UploadModeTabsProps = {
  mode: UploadMode;
  onModeChange: (mode: UploadMode) => void;
  textLabel: string;
  filesLabel: string;
};

export function UploadModeTabs(props: UploadModeTabsProps) {
  const tabBase = 'rounded-full px-4 py-2 text-sm font-semibold transition';

  return (
    <div class="inline-flex gap-2 rounded-full border border-border-soft bg-panel-soft p-1" role="tablist">
      <button
        class={`${tabBase} ${props.mode === 'text' ? 'bg-white text-ink shadow-tab' : 'text-muted hover:text-ink'}`}
        type="button"
        onClick={() => props.onModeChange('text')}
      >
        {props.textLabel}
      </button>
      <button
        class={`${tabBase} ${props.mode === 'file' ? 'bg-white text-ink shadow-tab' : 'text-muted hover:text-ink'}`}
        type="button"
        onClick={() => props.onModeChange('file')}
      >
        {props.filesLabel}
      </button>
    </div>
  );
}
