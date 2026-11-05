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
    hint: 'drop files anywhere in this box · or browse',
    drop: 'drop to add files',
    addFiles: 'add files',
    textItemName: 'content.txt',
    fileItemName: 'file',
    files: {
      count: '{{ count }} file(s)',
      remove: 'remove {{ name }}'
    },
    limit: 'limit {{ limit }}/file',
    guestListNote: "Sign in to keep links in your list. Guest links can't be listed later — copy it when it's ready.",
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
      regenerate: 'regenerate',
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
  links: {
    title: 'Your links',
    subline: 'Newest first. Copying a link never opens it.',
    new: 'New link',
    loading: 'Loading links...',
    loadFailed: 'Could not load your links.',
    loadFailedTitle: 'Unable to load your links',
    retry: 'retry',
    filterEmpty: 'No links in this filter.',
    copyNote:
      "Only the plain link can be copied here — the passphrase was shown once and isn't stored. Send it from the same place you saved it.",
    table: {
      created: 'created',
      status: 'status'
    },
    filters: {
      all: 'all',
      active: 'active',
      expiring: 'expiring',
      expired: 'expired'
    },
    status: {
      active: 'active',
      expiring: 'expiring',
      usedUp: 'used up',
      expired: 'expired'
    },
    protected: 'protected',
    unprotected: 'no passphrase',
    itemCount: '{{ count }} item',
    itemCountPlural: '{{ count }} items',
    minutes: '{{ count }} min',
    hours: '{{ count }} h',
    day: '{{ count }} day',
    days: '{{ count }} days',
    expiresIn: 'expires in {{ value }}',
    expiredAgo: 'expired {{ value }} ago',
    downloadLeft: '{{ count }} download left',
    downloadsLeft: '{{ count }} downloads left',
    unlimited: 'unlimited downloads',
    usedUpNote: '{{ current }} of {{ max }} access used · cleanup removes it',
    actions: {
      copyLink: 'copy link',
      delete: 'delete'
    },
    delete: {
      title: 'Delete “{{ name }}”?',
      bodyOne: "The link stops working immediately and the item is deleted. This can't be undone.",
      bodyMany: "The link stops working immediately and the {{ count }} items are deleted. This can't be undone.",
      confirm: 'Delete package',
      cancel: 'Cancel',
      failed: 'Could not delete the link.'
    },
    empty: {
      title: 'No links yet',
      body: 'Every link you create shows up here with the moment it was made and whether it still works.',
      cta: 'Create your first link'
    },
    login: {
      title: 'Sign in to see your links',
      body: "Links made without an account can't be listed here later. Copy them when you create them.",
      cta: 'Sign in',
      continueWithoutAccount: 'continue without an account'
    }
  },
  gate: {
    title: 'This link is protected',
    subtitle:
      "Enter the passphrase the sender shared with you. If there isn't one, leave the field empty. Nothing is loaded before you unlock.",
    passphraseLabel: 'passphrase',
    passphrasePlaceholder: 'passphrase',
    unlock: 'Unlock',
    tryAgain: 'Try again',
    note: 'Decrypted in your browser. The passphrase is never sent anywhere.',
    invalidTitle: "That passphrase doesn't work.",
    invalidBody: 'Check for stray spaces. Nothing was loaded and no access was used.',
    missingTitle: 'This link needs a passphrase.',
    missingBody: 'Enter the passphrase the sender shared with you. Nothing was loaded and no access was used.',
    goneTitle: 'This link is no longer available',
    goneBody:
      'It expired, its downloads were used up, or the sender deleted it. Content is removed by the cleanup job, not archived — ask the sender for a new link.',
    home: 'What is ShareIt?',
    rateLimitedTitle: 'Too many attempts.',
    rateLimitedBody: 'Wait a few minutes and try again. No access was used.',
    failedTitle: 'Unable to load this link.',
    failedBody: 'Check your connection and try again.'
  },
  share: {
    title: 'Shared content',
    loading: 'Loading content...',
    textLabel: 'text',
    filesLabel: 'files',
    textCount: '{{ count }} text',
    textCountPlural: '{{ count }} texts',
    fileCount: '{{ count }} file',
    fileCountPlural: '{{ count }} files',
    expiresIn: 'expires in {{ value }}',
    minutes: '{{ count }} min',
    hours: '{{ count }} h',
    day: '{{ count }} day',
    days: '{{ count }} days',
    expiresNow: 'expires now',
    unlimitedDownloads: 'unlimited downloads',
    downloadLeft: '{{ count }} download left',
    downloadsLeft: '{{ count }} downloads left',
    accessNote: "Unlocking used one access. This page won't refetch — keep the tab open.",
    fileFallback: 'item-{{ index }}',
    download: 'download',
    downloading: 'downloading…',
    retry: 'retry',
    textLoading: 'Loading text…',
    textErrorTitle: 'Could not read this text.',
    textErrorBody: 'The text could not be decrypted.',
    downloadFailedTitle: 'Download failed.',
    downloadFailedBody: 'The link may have expired between unlocking and downloading.'
  },
  auth: {
    signIn: 'Sign in',
    oidcNotConfiguredLong: 'OIDC is not configured. Set VITE_OIDC_* variables to enable login.',
    finishingSignIn: 'Finishing sign-in...',
    oidcNotConfigured: 'OIDC is not configured.'
  }
};
