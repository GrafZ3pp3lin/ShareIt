import { PillBadge } from '../../../shared/components/pill-badge';

type UploadHeroProps = {
  title: string;
  description: string;
  encryptionPill: string;
};

export function UploadHero(props: UploadHeroProps) {
  return (
    <div class="space-y-4">
      <div class="space-y-2">
        <h1 class="text-3xl font-semibold tracking-tight text-ink sm:text-4xl lg:text-5xl">{props.title}</h1>
        <p class="max-w-xl text-sm text-muted sm:text-base">{props.description}</p>
      </div>
      <PillBadge>{props.encryptionPill}</PillBadge>
    </div>
  );
}
