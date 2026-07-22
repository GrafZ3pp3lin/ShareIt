import IconInbox from '~icons/lucide/inbox';

type DashboardStageProps = {
  emptyLabel: string;
  notWiredLabel: string;
};

export function DashboardStage(props: DashboardStageProps) {
  return (
    <div class="grid justify-items-center gap-2 rounded-xl border border-dashed border-border-soft py-10 text-center">
      <IconInbox class="h-6 w-6 text-muted" />
      <div class="font-mono text-sm text-muted">{props.emptyLabel}</div>
      <div class="font-sans text-xs text-muted/80">{props.notWiredLabel}</div>
    </div>
  );
}
