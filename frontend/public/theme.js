// Theme bootstrap: runs before first paint so the `dark` class is already on
// <html> when styles apply. Loaded as a render-blocking classic script from
// index.html, deliberately not inlined: the CSP that ships with the deployment
// (shareit-csp in ../server-config's Traefik config) is a static string, and an
// inline script would pin a sha256 hash there that has to be bumped by hand with
// every edit here.
//
// Keep in step with THEME_STORAGE_KEY / resolveInitialTheme in
// src/shared/theme/theme.ts: an invalid stored value falls back to the system
// preference, exactly like the provider does after hydration.
(function () {
  var stored = localStorage.getItem('shareit.theme');
  var dark = stored === 'dark' || (stored !== 'light' && window.matchMedia('(prefers-color-scheme: dark)').matches);
  if (dark) document.documentElement.classList.add('dark');
})();
