import { describe, expect, it } from 'vitest';
import { Router } from '../../app/router.tsx';

const patternsFor = (url: string) => Router.match(url).map((match) => match.pattern);

describe('routeTree', () => {
  it('matches the composer at the root', () => {
    expect(patternsFor('/')).toHaveLength(2);
    expect(patternsFor('/')).not.toContain('/*');
  });

  it('matches the owner links list', () => {
    expect(patternsFor('/links')).toContain('/links');
  });

  it('matches the imprint page', () => {
    expect(patternsFor('/imprint')).toContain('/imprint');
  });

  it('matches the privacy page', () => {
    expect(patternsFor('/privacy')).toContain('/privacy');
  });

  it('extracts the package id for share links', () => {
    const shareMatch = Router.match('/s/0192f3ab-7cde-7def-8abc-0123456789ab').find(
      (match) => match.pattern === '/s/:packageId'
    );

    expect(shareMatch?.params).toEqual({ packageId: '0192f3ab-7cde-7def-8abc-0123456789ab' });
  });

  it('falls back to the not-found route', () => {
    expect(patternsFor('/missing/page')).toEqual(['/*']);
  });
});
