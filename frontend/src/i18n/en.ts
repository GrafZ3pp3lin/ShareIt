export const dict = {
  app: {
    brand: 'ShareIt',
    nav: {
      upload: 'upload',
      links: 'links',
      logout: 'log out'
    },
    copy: 'copy',
    copied: 'copied',
    notFound: {
      title: 'Page not found',
      body: 'The page you were looking for does not exist.',
      home: 'go home'
    },
    theme: {
      dark: 'dark',
      light: 'light',
      toggleLabel: 'Toggle theme'
    },
    footer: 'Share it once. Access it anywhere.'
  },
  composer: {
    headline: 'Paste it or drop it. One link.',
    subline: 'Text and files together. Expires when you say, encrypted if you want.',
    placeholder: 'paste or type your text',
    hint: 'drop files anywhere in this box · + browses',
    drop: 'drop to add files',
    addFiles: 'add files',
    textItemName: 'content.txt',
    fileItemName: 'file',
    files: {
      count: '{{ count }} file(s)',
      remove: 'remove {{ name }}'
    },
    limit: 'limit {{ limit }}/file',
    submit: 'Create link',
    submitting: 'Creating link…',
    cancel: 'Cancel',
    retry: 'retry upload',
    showLinkAnyway: 'show link anyway',
    progress: {
      encrypting: 'Encrypting {{ current }}/{{ total }}: {{ fileName }}',
      uploading: 'Uploading {{ current }}/{{ total }}: {{ percent }}%'
    },
    errors: {
      fileTooLarge: 'Skipped: {{ names }} exceed the {{ limit }} per-file limit.'
    },
    how: {
      title: 'how it works',
      one: 'Paste text, drop files — or both at once.',
      two: 'Pick how long it lives and who can open it.',
      three: 'Copy the link. It stops working when it should.',
      note: 'No account needed. With a password, everything is encrypted in your browser first.'
    }
  },
  options: {
    summary: 'expires {{ expiry }} · {{ downloads }} · password {{ password }}',
    expiry: {
      label: 'expires',
      sixHours: '6 hours',
      oneDay: '1 day',
      sevenDays: '7 days',
      thirtyDays: '30 days'
    },
    downloads: {
      label: 'downloads allowed',
      hint: 'one person, one download',
      once: 'once',
      count: '{{ count }} downloads',
      unlimited: 'unlimited'
    },
    password: {
      label: 'password',
      hint: 'encrypt text and files before upload',
      mode: {
        off: 'off',
        generate: 'generate',
        custom: 'custom'
      },
      off: 'off',
      generated: 'generated',
      custom: 'custom',
      regenerate: 'regenerate ↻',
      show: 'show',
      hide: 'hide',
      shownOnce: 'shown once on the result screen',
      suggestion: '5 random words is plenty',
      note: 'Encrypts text and files in your browser. The passphrase is never sent to the server — if you lose it, the content is gone.',
      errors: {
        tooShort: 'Use at least 12 characters.'
      },
      strength: {
        weak: 'weak',
        ok: 'ok',
        strong: 'strong'
      }
    },
    guestNote: 'Expiry and downloads are fixed for links created without an account. Sign in to choose them.'
  },
  result: {
    ready: 'link ready',
    summary: 'expires {{ expiry }} · {{ downloads }} · password {{ password }}',
    link: 'link',
    passphrase: 'passphrase',
    combined: 'link + passphrase — both included',
    selfOpenTitle: "Don't open this link yourself.",
    selfOpenBody:
      'Opening it counts as an access: it uses up one of the allowed downloads, and with a one-time link the upload is deleted by the cleanup job after you look at it.',
    channels:
      'Anyone holding this single link has everything. For maximum security, send the link and the passphrase through different channels — link by mail, passphrase by chat, for example.',
    noPasswordNote:
      "Anyone with the link can open it — including you, and that counts as an access. Don't test it on yourself.",
    incomplete: 'Some items did not finish uploading — the link may be missing them.',
    another: 'Upload another'
  },
  errors: {
    createPackage: 'Failed to create the link.',
    upload: 'Failed to upload {{ fileName }}.',
    encrypt: 'Unable to encrypt {{ fileName }}.',
    canceled: 'Upload canceled.',
    missingContent: 'Add text or a file first.'
  },
  share: {
    title: 'Shared content',
    loading: 'Loading content...',
    loadFailed: 'Unable to load this package.',
    packageLabel: 'Package {{ id }}',
    itemsLabel: 'Items: {{ count }}',
    textEntry: 'Text entry',
    content: 'Content',
    encryptedNotice: 'This text is encrypted. Enter the passphrase to reveal it.',
    encryptedFileNotice: 'This file is encrypted. Enter the passphrase, then download to decrypt it.',
    passphrasePlaceholder: 'Passphrase',
    decrypting: 'Decrypting...',
    decrypt: 'Decrypt',
    enterPassphrase: 'Enter a passphrase.',
    invalidPassphrase: 'Invalid passphrase.',
    downloadFile: 'Download file',
    downloadFailed: 'Failed to download content.',
    noDownloadToken: 'No download token available.',
    defaultFileName: 'shareit-download'
  },
  auth: {
    signIn: 'Sign in',
    signInDescription: 'Log in to see and manage the content you uploaded.',
    oidcNotConfiguredLong: 'OIDC is not configured. Set VITE_OIDC_* variables to enable login.',
    alreadySignedIn: 'You are already signed in.',
    continueWithOidc: 'Continue with OIDC',
    finishingSignIn: 'Finishing sign-in...',
    oidcNotConfigured: 'OIDC is not configured.'
  },
  account: {
    notWired: 'Backend listing endpoints are not wired yet.',
    dashboard: {
      empty: 'no uploads yet'
    },
    login: {
      heading: 'log in',
      body: 'to view and manage your uploads',
      cta: 'log in →',
      continueWithoutAccount: 'continue without an account'
    }
  }
};
