/** The texts of the attachments demo page (`attachments.demo.*`); the widget's own texts are `attachments.widget.*`. */
export const AttachmentsDemoTranslationKeys = {
  Title: 'attachments.demo.title',
  Intro: 'attachments.demo.intro',
} as const;

/** Every key the feature renders, for the bundle check. */
export const ATTACHMENTS_DEMO_TRANSLATION_KEYS: readonly string[] = Object.values(AttachmentsDemoTranslationKeys);
