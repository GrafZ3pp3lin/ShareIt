export const dict = {
  app: {
    brand: 'ShareIt',
    nav: {
      upload: 'upload',
      myFiles: 'my files',
      logout: 'log out'
    },
    theme: {
      dark: 'dark',
      light: 'light',
      toggleLabel: 'Toggle theme'
    },
    footer: 'Share it once. Access it anywhere.'
  },
  upload: {
    windowLabel: 'shareit — share',
    panels: {
      fileLabel: 'FILE',
      textLabel: 'TEXT / SNIPPET'
    },
    filePanel: {
      prompt: 'Drop file or browse',
      selected: '{{ count }} file(s) selected'
    },
    placeholders: {
      title: 'Title (optional)',
      description: 'Description (optional)',
      text: 'Paste or type your text'
    },
    details: {
      toggle: '+ add title & description'
    },
    expiry: {
      label: 'expires'
    },
    password: {
      label: 'password',
      on: 'on',
      off: 'off',
      caption: 'Encrypts pasted text client-side before upload. Files are not encrypted.'
    },
    submitCommand: 'upload --get-link',
    submit: {
      uploading: 'Uploading...'
    },
    result: {
      heading: 'link ready',
      copy: 'copy',
      copied: 'copied',
      captionPlain: 'expires {{ expiry }}',
      captionWithPassword: 'expires {{ expiry }} · password set',
      another: 'Upload another'
    },
    passphraseLabel: 'Passphrase (shown once):',
    copyPassphrase: 'Copy passphrase',
    errors: {
      createPackage: 'Failed to create package.',
      missingContent: 'Add text or select at least one file before uploading.',
      encryptFailed: 'Unable to encrypt the text.',
      addTextFailed: 'Failed to add text.',
      uploadFilesFailed: 'Failed to upload files.'
    }
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
    windowLabel: 'shareit — my files',
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
