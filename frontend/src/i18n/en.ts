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
    footer: 'Encrypted in your browser, shared by link.'
  },
  composer: {
    headline: 'Paste it or drop it. One link.',
    subline: 'Encrypted in your browser by default.',
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
    haveCode: 'Have a code?',
    codePlaceholder: 'ABC-234',
    codeSubmit: 'open',
    codeOpening: 'opening…',
    codeInvalid: 'Enter a 6-character code.',
    codeNotFound: 'No package has this code.',
    codeRateLimited: 'Too many attempts. Try again in {{ wait }}.',
    codeRateLimitedUnknown: 'Too many attempts. Please try again later.',
    codeRetryWaitSeconds: '{{ count }}s',
    codeRetryWaitMinutes: '{{ count }} min',
    codeFailed: 'Could not open this code. Check your connection and try again.',
    submit: 'Create link',
    submitting: 'Creating link…',
    cancel: 'Cancel',
    retry: 'retry upload',
    progress: {
      label: 'Upload progress',
      encrypting: 'Encrypting {{ current }}/{{ total }}: {{ fileName }}',
      uploading: 'Uploading {{ current }}/{{ total }}: {{ percent }}%'
    },
    errors: {
      emptyFile: 'Skipped: {{ names }} — empty files cannot be uploaded.',
      fileTooLarge: 'Skipped: {{ names }} exceed the {{ limit }} per-file limit.',
      tooManyItems: 'This package can hold at most {{ limit }} items; {{ count }} are selected.',
      tooManyItemsSkipped: 'Skipped: {{ names }} — this package can hold at most {{ limit }} items.'
    },
    how: {
      title: 'how it works',
      one: 'Paste text, drop files, or both.',
      two: 'Set the expiry and access limit.',
      three: 'Copy the link and send it.'
    }
  },
  options: {
    summary: 'expires in {{ expiry }} · {{ downloads }} · {{ password }}',
    expiry: {
      label: 'expires',
      sixHours: '6 hours',
      oneDay: '1 day',
      sevenDays: '7 days',
      thirtyDays: '30 days',
      tenMinutes: '10 minutes'
    },
    downloads: {
      label: 'accesses allowed',
      hint: 'one person, one access',
      once: 'one access',
      count: '{{ count }} accesses',
      unlimited: 'unlimited accesses'
    },
    password: {
      label: 'password',
      hint: 'encrypt text and files before upload',
      mode: {
        off: 'off',
        generate: 'generate',
        custom: 'custom',
        code: 'code'
      },
      off: 'no password',
      generated: 'use generated password',
      custom: 'use custom password',
      code: 'use a code',
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
    code: {
      guardrails:
        'Code mode generates a 6-character code • single use • valid for 10 minutes. It is less secure and meant for devices where you cannot share a link.'
    },
    guestNote: 'Expiry and accesses are fixed for links created without an account. Sign in to choose them.'
  },
  result: {
    ready: 'link ready',
    summary: 'expires in {{ expiry }} · {{ downloads }} · {{ password }}',
    code: 'code',
    codeNote: 'Single use, valid for 10 minutes. Enter it on the home page to open the package.',
    codeSelfOpenTitle: "Don't enter this code yourself.",
    codeSelfOpenBody:
      'It can only be opened once. Test your uploads with a copy you are prepared to lose, or share the code directly with the recipient.',
    link: 'link',
    passphrase: 'passphrase',
    combined: 'link + passphrase — both included',
    selfOpenTitle: "Don't open this link yourself.",
    selfOpenBody:
      'Opening it counts as an access: it uses up one of the allowed accesses, and with a one-time link the upload is deleted by the cleanup job after its retention window.',
    channels:
      'Anyone holding this single link has everything. For maximum security, send the link and the passphrase through different channels — link by mail, passphrase by chat, for example.',
    noPasswordNote:
      "Anyone with the link can open it — including you, and that counts as an access. Don't test it on yourself.",
    another: 'Upload another'
  },
  errors: {
    createPackage: 'Failed to create the link.',
    createCode: 'Could not create an access code.',
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
    loadMore: 'load more',
    loadingMore: 'loading…',
    loadMoreFailed: 'Could not load more links.',
    filterEmpty: 'No links in this filter.',
    copyNote:
      'Links can always be copied. A code can be copied only in the tab that created it, until it expires; the passphrase is never stored.',
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
    code: 'code',
    itemCount: '{{ count }} item',
    itemCountPlural: '{{ count }} items',
    minutes: '{{ count }} min',
    hours: '{{ count }} h',
    day: '{{ count }} day',
    days: '{{ count }} days',
    expiresIn: 'expires in {{ value }}',
    expiredAgo: 'expired {{ value }} ago',
    downloadLeft: '{{ count }} access left',
    downloadsLeft: '{{ count }} accesses left',
    unlimited: 'unlimited accesses',
    usedUpNote: '{{ current }} of {{ max }} accesses used · cleanup removes it',
    actions: {
      copyLink: 'copy link',
      copyCode: 'copy code',
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
    subtitle: "Enter the passphrase the sender shared with you. If there isn't one, leave the field empty.",
    passphraseLabel: 'passphrase',
    passphrasePlaceholder: 'passphrase',
    unlock: 'Unlock',
    tryAgain: 'Try again',
    note: 'Decrypted in your browser. The passphrase is never sent anywhere.',
    invalidTitle: "That passphrase doesn't work.",
    invalidBody: 'Check for stray spaces.',
    missingTitle: 'This link needs a passphrase.',
    missingBody: 'Enter the passphrase the sender shared with you.',
    goneTitle: 'This link is no longer available',
    goneBody: 'It expired, its accesses were used up, or the sender deleted it. Ask the sender for a new link.',
    home: 'What is ShareIt?',
    rateLimitedTitle: 'Too many attempts.',
    rateLimitedBody: 'Wait a few minutes and try again. No access was used.',
    sessionExpiredTitle: 'This access has expired.',
    sessionExpiredBody:
      'Unlock the link again to continue. A one-time link may already be used up — ask the sender for a new one.',
    failedTitle: 'Unable to load this link.',
    failedBody: 'Check your connection and try again.'
  },
  share: {
    title: 'Shared content',
    invalidLinkTitle: 'This link is not valid',
    invalidLinkBody: 'Check the link, or enter the six-character code on the home page.',
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
    accessNote: 'This access is valid until {{ time }}.',
    fileFallback: 'item-{{ index }}',
    download: 'download',
    downloading: 'downloading…',
    retry: 'retry',
    textLoading: 'Loading text…',
    textErrorTitle: 'Could not read this text.',
    textErrorBody: 'The text could not be decrypted.',
    downloadFailedTitle: 'Download failed.',
    downloadFailedBody: 'The access session may have expired. Reopen the link to try again.'
  },
  auth: {
    signIn: 'Sign in',
    oidcNotConfiguredLong: 'OIDC is not configured. Set VITE_OIDC_* variables to enable login.',
    finishingSignIn: 'Finishing sign-in...',
    oidcNotConfigured: 'OIDC is not configured.'
  }
};
