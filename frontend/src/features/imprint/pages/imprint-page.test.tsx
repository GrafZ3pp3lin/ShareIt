import { render, screen } from '@solidjs/testing-library';
import { describe, expect, it } from 'vitest';
import { I18nProvider } from '../../../i18n/i18n.tsx';
import { ImprintPage } from './imprint-page.tsx';

describe('<ImprintPage />', () => {
  it('renders the service provider and contact details', () => {
    render(() => (
      <I18nProvider>
        <ImprintPage />
      </I18nProvider>
    ));

    expect(screen.getByRole('heading', { name: 'Imprint' })).toBeInTheDocument();
    expect(screen.getByText('Johannes Gaiser')).toBeInTheDocument();
    expect(screen.getByText('Horber Weg 4')).toBeInTheDocument();
    expect(screen.getByText('72280 Dornstetten')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'shareit@kroet.de' })).toHaveAttribute('href', 'mailto:shareit@kroet.de');
    expect(screen.getByRole('link', { name: 'GitHub issues' })).toHaveAttribute(
      'href',
      'https://github.com/GrafZ3pp3lin/ShareIt/issues'
    );
    expect(screen.queryByText('Responsible for the content')).not.toBeInTheDocument();
  });
});
