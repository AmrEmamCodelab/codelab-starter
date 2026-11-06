// Flow 4 (phase-5 exit criterion): a visitor's contact message returns a CU-YYYY-NNN reference number and notifies
// the contact inbox; FAQ content the admin edits shows on the public portal in ar and en. The FAQ titles are put back
// at the end, so the flow can run again.
import {
  ApiPaths,
  AppPaths,
  FaqTabLabels,
  FLOW_LANGUAGES,
  FlowData,
  FlowTimings,
  HttpMethods,
  MAX_SUCCESS_STATUS,
  Sel,
} from '../lib/flow-constants.mjs';
import { contactEmail, contactReferenceFor, CONTACT_REFERENCE_PATTERN, faqMarkers } from '../lib/flow-checks.mjs';
import { signIn, waitForMail } from '../lib/flow-runtime.mjs';
import { setTimeout as delay } from 'node:timers/promises';

const ADMIN_LANGUAGE = 'en';
const FIRST_OPTION_WITH_VALUE = 1;

function succeeded(page, method, fragment) {
  return page.waitForResponse((response) => response.request().method() === method && response.url().includes(fragment) && response.status() <= MAX_SUCCESS_STATUS);
}

/** Opens the FAQ tab's first section, writes the titles, confirms the dialog and saves the page. */
async function writeFirstFaqTitles(page, titles) {
  await page.locator(Sel.ContentTabs).getByRole('tab', { name: FaqTabLabels[ADMIN_LANGUAGE] }).click();
  await page.locator(Sel.ContentSectionOpen).first().click();
  const previous = { ar: await page.locator(Sel.ContentTitleAr).inputValue(), en: await page.locator(Sel.ContentTitleEn).inputValue() };
  await page.locator(Sel.ContentTitleAr).fill(titles.ar);
  await page.locator(Sel.ContentTitleEn).fill(titles.en);
  await page.locator(Sel.OpenDialogConfirm).click();
  const saved = succeeded(page, HttpMethods.Put, ApiPaths.AdminPortalContentPrefix);
  await page.locator(Sel.ContentSave).click();
  await saved;
  await page.locator(Sel.ContentSaved).waitFor();
  return previous;
}

/** Reloads the page until the text shows (the public content may be served from a short cache). */
async function waitForText(page, url, text) {
  const deadline = Date.now() + FlowTimings.ContentWaitMs;
  for (;;) {
    await page.goto(url);
    await page.locator(Sel.PortalMain).waitFor();
    if ((await page.getByText(text, { exact: false }).count()) > 0) return;
    if (Date.now() > deadline) throw new Error(`"${text}" did not show on ${url} within ${FlowTimings.ContentWaitMs} ms.`);
    await delay(FlowTimings.ContentPollMs);
  }
}

export async function runContactFaq({ recorder, credentials, options }) {
  const startedMs = Date.now();
  const markers = faqMarkers(options.runStamp);
  const visitor = await recorder.openPage(ADMIN_LANGUAGE);
  const admin = await recorder.openPage(ADMIN_LANGUAGE);
  let reference = null;
  let originalTitles = null;

  await recorder.step('A visitor submits the contact form', visitor, async () => {
    await visitor.goto(recorder.url(AppPaths.PortalContact));
    await visitor.locator(Sel.ContactFullName).fill(FlowData.ContactName);
    await visitor.locator(Sel.ContactEmail).fill(contactEmail(options.runStamp));
    const topic = visitor.locator(`${Sel.ContactTopic} option`).nth(FIRST_OPTION_WITH_VALUE);
    await visitor.locator(Sel.ContactTopic).selectOption(await topic.getAttribute('value'));
    await visitor.locator(Sel.ContactMessage).fill(FlowData.ContactMessage);
    const filed = succeeded(visitor, HttpMethods.Post, ApiPaths.ContactMessages);
    await visitor.locator(Sel.ContactSubmit).click();
    await filed;
    return 'accepted';
  });

  await recorder.step('The page shows a CU-YYYY-NNN reference number of this year', visitor, async () => {
    const feedback = visitor.locator(Sel.ContactFeedback);
    await feedback.getByText(/CU-\d{4}-\d+/).first().waitFor();
    reference = contactReferenceFor(await feedback.innerText(), new Date().getFullYear());
    if (reference === null) throw new Error(`No reference number of ${new Date().getFullYear()} in: ${await feedback.innerText()}`);
    return reference;
  });

  await recorder.step('Admin signs in', admin, async () => {
    await signIn(admin, recorder, credentials);
    return `signed in as ${credentials.user}`;
  });

  await recorder.step('The contact inbox is notified (e-mail naming the reference number)', admin, async () => {
    const mail = await waitForMail({ mailDir: options.mailDir, filter: { subjectIncludes: reference, notBeforeMs: startedMs }, adminPage: admin, recorder });
    return `to ${mail.to}: "${mail.subject}"`;
  });

  await recorder.step('Admin edits the first FAQ title in ar and en and saves', admin, async () => {
    await admin.goto(recorder.url(AppPaths.AdminPortalContent));
    originalTitles = await writeFirstFaqTitles(admin, markers);
    return `ar "${markers.ar}", en "${markers.en}"`;
  });

  for (const language of FLOW_LANGUAGES) {
    const reader = await recorder.openPage(language);
    await recorder.step(`The public FAQ shows the edit (${language})`, reader, async () => {
      await waitForText(reader, recorder.url(AppPaths.PortalFaq), markers[language]);
      return markers[language];
    });
  }

  await recorder.cleanup('Clean-up: the FAQ titles are put back', admin, originalTitles !== null, async () => {
    await admin.goto(recorder.url(AppPaths.AdminPortalContent));
    await writeFirstFaqTitles(admin, originalTitles);
    return 'restored';
  });
}
