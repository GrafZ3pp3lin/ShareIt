import { render, screen } from '@solidjs/testing-library';
import { describe, expect, it } from 'vitest';
import { I18nProvider, useI18n } from './i18n.tsx';

const Probe = () => {
  const { t } = useI18n();
  return <span>{t('composer.files.count', { count: 2 })}</span>;
};

describe('useI18n', () => {
  it('interpolates placeholders inside a provider', () => {
    render(() => (
      <I18nProvider>
        <Probe />
      </I18nProvider>
    ));

    expect(screen.getByText('2 file(s)')).toBeInTheDocument();
  });
});
