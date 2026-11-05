import { render, screen } from '@solidjs/testing-library';
import { describe, expect, it } from 'vitest';
import { I18nProvider } from '../../../i18n/i18n.tsx';
import { NotFoundPage } from './not-found-page.tsx';

describe('<NotFoundPage />', () => {
  it('renders the not-found message with a link home', () => {
    render(() => (
      <I18nProvider>
        <NotFoundPage />
      </I18nProvider>
    ));

    expect(screen.getByRole('heading', { name: 'Page not found' })).toBeInTheDocument();
    expect(screen.getByText('The page you were looking for does not exist.')).toBeInTheDocument();
    expect(screen.getByText('go home').closest('a')).not.toBeNull();
  });
});
