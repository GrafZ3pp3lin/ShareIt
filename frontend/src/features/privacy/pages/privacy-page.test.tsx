import { render, screen } from '@solidjs/testing-library';
import { describe, expect, it } from 'vitest';
import { I18nProvider } from '../../../i18n/i18n.tsx';
import { PrivacyPage } from './privacy-page.tsx';

describe('<PrivacyPage />', () => {
  it('renders the controller details, browser storage disclosures, and data subject rights', () => {
    render(() => (
      <I18nProvider>
        <PrivacyPage />
      </I18nProvider>
    ));

    expect(screen.getByRole('heading', { level: 1, name: 'Privacy policy' })).toBeInTheDocument();
    expect(screen.getByText('Johannes Gaiser')).toBeInTheDocument();
    expect(screen.getByText('Horber Weg 4')).toBeInTheDocument();

    const emailLinks = screen.getAllByRole('link', { name: 'shareit@kroet.de' });
    expect(emailLinks).toHaveLength(2);
    expect(emailLinks[0]).toHaveAttribute('href', 'mailto:shareit@kroet.de');

    expect(screen.getByRole('heading', { name: 'Web analytics' })).toBeInTheDocument();
    expect(screen.getByText('/m/')).toBeInTheDocument();
    expect(screen.queryByText('umami.kroet.de')).not.toBeInTheDocument();

    expect(screen.getByRole('heading', { name: 'Cookies and browser storage' })).toBeInTheDocument();
    expect(screen.getByText('shareit.theme')).toBeInTheDocument();
    expect(screen.queryByText('shareit-guest-id')).not.toBeInTheDocument();
    expect(screen.getByText('IndexedDB')).toBeInTheDocument();

    expect(screen.getByRole('heading', { name: 'Your rights' })).toBeInTheDocument();
    expect(screen.getByText(/Right to object \(Art\. 21 GDPR\)/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Landesbeauftragte für den Datenschutz/ })).toHaveAttribute(
      'href',
      'https://www.baden-wuerttemberg.datenschutz.de'
    );
  });
});
