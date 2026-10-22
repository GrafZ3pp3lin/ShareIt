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
      selected: '{{ count }} file(s) selected',
      limit: 'limit {{ limit }}/file'
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
      caption: 'Encrypts pasted text and files client-side before upload. The passphrase is shown once.'
    },
    submitCommand: 'upload --get-link',
    submit: {
      uploading: 'Uploading...',
      encrypting: 'Encrypting {{ current }}/{{ total }}: {{ fileName }}',
      uploadingFile: 'Uploading {{ current }}/{{ total }}: {{ fileName }} · {{ percent }}%',
      cancel: 'Cancel'
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
      encryptFileFailed: 'Unable to encrypt the files.',
      addTextFailed: 'Failed to add text.',
      uploadFilesFailed: 'Failed to upload files.',
      fileTooLarge: 'Skipped: {{ names }} exceed the {{ limit }} per-file limit.',
      canceled: 'Upload canceled.'
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
