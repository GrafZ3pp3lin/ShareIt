import type { ParentProps } from 'solid-js';
import { useI18n } from '../../../i18n/i18n.tsx';
import { operator } from '../../../shared/legal/operator.ts';

const linkClass = 'text-brand underline decoration-brand/40 underline-offset-2 transition hover:text-brand-hover';
const paragraphClass = 'text-sm leading-relaxed text-pretty text-ink';
const listClass = 'grid list-disc gap-1 pl-5 text-sm leading-relaxed text-pretty text-ink marker:text-muted';

function Section(props: ParentProps<{ title: string }>) {
  return (
    <section class="grid gap-2">
      <h2 class="text-base font-semibold text-ink">{props.title}</h2>
      {props.children}
    </section>
  );
}

export function PrivacyPage() {
  const { t } = useI18n();

  return (
    <article class="mx-auto grid w-full max-w-[68ch] gap-7">
      <header class="grid gap-1.5">
        <h1 class="text-2xl font-semibold tracking-tight text-ink">{t('privacy.title')}</h1>
        <p class="max-w-[68ch] text-sm leading-relaxed text-muted text-pretty">{t('privacy.intro')}</p>
      </header>

      <Section title="Controller (Art. 13(1)(a) GDPR)">
        <address class="grid text-sm not-italic text-ink">
          <span>{operator.name}</span>
          <span>{operator.street}</span>
          <span>{operator.city}</span>
          <span>{operator.country}</span>
          <a class={`${linkClass} mt-1 w-fit`} href={`mailto:${operator.email}`}>
            {operator.email}
          </a>
        </address>
      </Section>

      <Section title="Server logs and rate limiting">
        <p class={paragraphClass}>
          When you access ShareIt, our reverse proxy and API process technical connection data (IP address, date and
          time, HTTP method and path, status code, response duration, and user agent). Your IP address is also kept in
          memory for short-term rate limiting (1 to 15 minutes) and is not stored in the database.
        </p>
        <p class={paragraphClass}>
          This processing is necessary to deliver the website, troubleshoot errors, and protect the service against
          abuse (Art. 6(1)(f) GDPR; legitimate interest in secure and reliable operation). Access and application logs
          are retained for up to 30 days, and request traces for up to 14 days.
        </p>
      </Section>

      <Section title="Web analytics">
        <p class={paragraphClass}>
          We use self-hosted Umami (<code class="font-mono text-xs">umami.kroet.de</code>) for aggregate usage
          statistics. It sets no cookies, uses no browser storage (§ 25 TDDDG), and records the page path (excluding URL
          hashes and query parameters), referrer, browser, OS, device type, screen size, and language. Your IP address
          is used only transiently to derive a rotating, non-reversible session hash and is not stored in Umami. The
          legal basis is Art. 6(1)(f) GDPR (legitimate interest in understanding usage and maintaining the service).
        </p>
      </Section>

      <Section title="Packages, guest use, and sign-in">
        <p class={paragraphClass}>
          Without an account, your browser generates a random UUID (
          <code class="font-mono text-xs">shareit-guest-id</code>) and sends it as the owner or client ID. Signing in is
          optional and uses our self-hosted Keycloak identity provider (
          <code class="font-mono text-xs">keycloak.kroet.de</code>) with the{' '}
          <code class="font-mono text-xs">openid</code> scope; Keycloak processes your login credentials and account
          data, while ShareIt stores only your account UUID (<code class="font-mono text-xs">sub</code> claim) to list
          and manage your links.
        </p>
        <p class={paragraphClass}>
          When you create a package, the server stores the package and item IDs, owner ID, item kind (text or file) and
          byte size, timestamps, expiry, access limit and count, the uploaded content and metadata blobs (encrypted in
          your browser by default, or plaintext if you turn encryption off), and—for protected packages—an unlock
          verifier (plus a three-character server locator in code mode). Opening a package creates a 15-minute access
          session storing a SHA-256 token hash and the opener’s guest or user ID.
        </p>
        <p class={paragraphClass}>
          We process this data to provide the sharing and account features you request (Art. 6(1)(b) GDPR) and to
          prevent unauthorized access and abuse (Art. 6(1)(f) GDPR). Technical connection data and a guest ID or login
          are technically required to create or open packages (Art. 13(2)(e) GDPR).
        </p>
        <p class={paragraphClass}>
          Packages and their stored blobs are automatically deleted by a background cleanup job (running every 5
          minutes) once they expire (up to 6 hours for guests, up to 30 days for signed-in users, 10 minutes for access
          codes), 30 minutes after reaching their access limit, or when deleted by a signed-in owner. Access sessions
          expire after 15 minutes. Keycloak account data is kept until your account is deleted.
        </p>
      </Section>

      <Section title="Cookies and browser storage">
        <p class={paragraphClass}>
          ShareIt does not use analytics, tracking, or advertising cookies. It uses only strictly necessary browser
          storage and login cookies under § 25(2) no. 2 TDDDG and Art. 6(1)(b) and (f) GDPR:
        </p>
        <ul class={listClass}>
          <li>
            <code class="font-mono text-xs">localStorage</code>: theme preference (
            <code class="font-mono text-xs">shareit.theme</code>), guest UUID (
            <code class="font-mono text-xs">shareit-guest-id</code>), and OIDC login state.
          </li>
          <li>
            <code class="font-mono text-xs">sessionStorage</code>: 15-minute package access tokens (
            <code class="font-mono text-xs">shareit-access-session:*</code>), 10-minute access codes in the creating tab
            (<code class="font-mono text-xs">shareit-short-code:*</code>), and OIDC login state.
          </li>
          <li>
            <code class="font-mono text-xs">IndexedDB</code>: non-extractable DPoP cryptographic key pair binding login
            tokens to your browser.
          </li>
          <li>Cookies: session cookies set by Keycloak only when you sign in.</li>
        </ul>
      </Section>

      <Section title="Recipients and hosting">
        <p class={paragraphClass}>
          The application, database, blob storage, Keycloak, telemetry, and analytics run on a server hosted in Germany;
          the hosting provider acts as a processor under Art. 28 GDPR. Anyone with whom you share a package link (and
          passphrase, if protected) or access code can access and download the shared content.
        </p>
        <p class={paragraphClass}>
          If you contact us by email, your address and message are processed via our email provider (Art. 28 GDPR)
          solely to handle your inquiry (Art. 6(1)(b) or (f) GDPR) and deleted once resolved unless statutory retention
          obligations apply.
        </p>
      </Section>

      <Section title="Your rights">
        <p class={paragraphClass}>
          Under the GDPR you have the right of access (Art. 15), rectification (Art. 16), erasure (Art. 17), restriction
          of processing (Art. 18), and data portability (Art. 20). To exercise your rights, email{' '}
          <a class={linkClass} href={`mailto:${operator.email}`}>
            {operator.email}
          </a>{' '}
          (for guest packages, please include the package link or your{' '}
          <code class="font-mono text-xs">shareit-guest-id</code> so the record can be identified).
        </p>
        <p class={`${paragraphClass} font-medium`}>
          Right to object (Art. 21 GDPR): You have the right to object at any time, on grounds relating to your
          particular situation, to the processing of your personal data based on Art. 6(1)(f) GDPR.
        </p>
        <p class={paragraphClass}>
          You also have the right to lodge a complaint with a data protection supervisory authority (Art. 77 GDPR). The
          competent authority for the controller is the{' '}
          <a class={linkClass} href="https://www.baden-wuerttemberg.datenschutz.de">
            Landesbeauftragte für den Datenschutz und die Informationsfreiheit Baden-Württemberg
          </a>
          , Heilbronner Straße 35, 70191 Stuttgart, Germany (
          <a class={linkClass} href="mailto:poststelle@lfdi.bwl.de">
            poststelle@lfdi.bwl.de
          </a>
          ).
        </p>
      </Section>
    </article>
  );
}
