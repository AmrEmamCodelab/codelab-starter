// Flow 2 (phase-5 exit criterion): the admin sends a test notification; it shows in the bell with the template's
// wording in ar and in en, and its e-mail lands in the e-mail pickup folder (FileDrop: the local SMTP sink).
import { ApiPaths, AppPaths, HttpMethods, MAX_SUCCESS_STATUS, Sel, TestNotificationTitles } from '../lib/flow-constants.mjs';
import { signIn, waitForMail } from '../lib/flow-runtime.mjs';

const FIRST_LANGUAGE = 'en';
const SECOND_LANGUAGE = 'ar';

async function bellShows(page, title) {
  await page.locator(Sel.Bell).click();
  await page.locator(Sel.BellItemTitle).filter({ hasText: title }).first().waitFor();
  return `the bell lists "${title}"`;
}

export async function runNotificationBell({ recorder, credentials, options }) {
  const startedMs = Date.now();
  const page = await recorder.openPage(FIRST_LANGUAGE);

  await recorder.step('Admin signs in (en)', page, async () => {
    await signIn(page, recorder, credentials);
    return `signed in as ${credentials.user}`;
  });

  await recorder.step('Admin sends a test notification from the notifications page', page, async () => {
    await page.goto(recorder.url(AppPaths.Notifications));
    // The route is lazy: count() right after goto() sees a blank page, so wait for the heading first.
    await page.locator(Sel.NotificationsTitle).waitFor();
    const control = page.locator(Sel.SendTestNotification);
    if ((await control.count()) === 0) {
      throw new Error(`The notifications page has no "send test notification" control (${Sel.SendTestNotification}) for POST ${ApiPaths.NotificationsTest}.`);
    }
    const sent = page.waitForResponse(
      (response) => response.request().method() === HttpMethods.Post && response.url().includes(ApiPaths.NotificationsTest) && response.status() <= MAX_SUCCESS_STATUS,
    );
    await control.first().click();
    await sent;
    return 'accepted (202)';
  });

  await recorder.step('The bell shows it with the English template wording', page, () => bellShows(page, TestNotificationTitles[FIRST_LANGUAGE]));

  const arabic = await recorder.openPage(SECOND_LANGUAGE);
  await recorder.step('The bell shows it with the Arabic template wording', arabic, async () => {
    await signIn(arabic, recorder, credentials);
    return bellShows(arabic, TestNotificationTitles[SECOND_LANGUAGE]);
  });

  await recorder.step('Its e-mail is dropped in the pickup folder', page, async () => {
    const mail = await waitForMail({
      mailDir: options.mailDir,
      filter: { subjectIncludes: Object.values(TestNotificationTitles), notBeforeMs: startedMs },
      adminPage: page,
      recorder,
    });
    return `to ${mail.to}: "${mail.subject}"`;
  });
}
