// Flow 3 (phase-5 exit criterion): an Excel-imported user receives the activation e-mail, opens the link, sets a
// password and signs in; opening the same link again is refused.
import {
  ApiPaths,
  AppPaths,
  HttpMethods,
  IMPORT_FILE_NAME,
  MAX_SUCCESS_STATUS,
  Sel,
  XLSX_MIME,
} from '../lib/flow-constants.mjs';
import { activationPassword, importedUser, importRows } from '../lib/flow-checks.mjs';
import { signIn, waitForMail } from '../lib/flow-runtime.mjs';
import { findLink } from '../lib/mail.mjs';
import { buildXlsx } from '../lib/xlsx.mjs';

const LANGUAGE = 'en';

function succeeded(page, method, matches) {
  return page.waitForResponse((response) => response.request().method() === method && matches(response.url()) && response.status() <= MAX_SUCCESS_STATUS);
}

async function setPassword(page, password) {
  await page.locator(Sel.PasswordInput).fill(password);
  await page.locator(Sel.ConfirmPasswordInput).fill(password);
  await page.locator(Sel.SubmitButton).click();
}

export async function runActivation({ recorder, credentials, options }) {
  const startedMs = Date.now();
  const user = importedUser(options.runStamp);
  const chosenPassword = activationPassword(options.runStamp);
  recorder.secrets.push(chosenPassword);
  const admin = await recorder.openPage(LANGUAGE);
  let link = null;

  await recorder.step('Admin signs in', admin, async () => {
    await signIn(admin, recorder, credentials);
    return `signed in as ${credentials.user}`;
  });

  await recorder.step(`Admin imports ${user.userName} from an Excel sheet (preview)`, admin, async () => {
    await admin.goto(recorder.url(AppPaths.AdminImport));
    await admin.locator(Sel.ImportFile).setInputFiles({ name: IMPORT_FILE_NAME, mimeType: XLSX_MIME, buffer: buildXlsx(importRows(user)) });
    const previewed = succeeded(admin, HttpMethods.Post, (url) => url.includes(ApiPaths.UserImportPreview));
    await admin.locator(Sel.ImportPreview).click();
    await previewed;
    return (await admin.locator(Sel.ImportSummary).innerText()).trim();
  });

  await recorder.step('Admin commits the import', admin, async () => {
    const committed = succeeded(admin, HttpMethods.Post, (url) => url.includes(ApiPaths.UserImportPrefix) && url.endsWith(ApiPaths.UserImportCommitSuffix));
    await admin.locator(Sel.ImportCommit).click();
    await committed;
    return 'committed';
  });

  await recorder.step('The activation e-mail is dropped for the new user', admin, async () => {
    const mail = await waitForMail({ mailDir: options.mailDir, filter: { to: user.email, notBeforeMs: startedMs }, adminPage: admin, recorder });
    link = findLink(mail, AppPaths.Activate);
    if (link === null) throw new Error(`The mail "${mail.subject}" has no ${AppPaths.Activate} link.`);
    recorder.secrets.push(link);
    return `to ${mail.to}: "${mail.subject}" with an activation link`;
  });

  const visitor = await recorder.openPage(LANGUAGE);
  await recorder.step('The user opens the link and sets a password', visitor, async () => {
    await visitor.goto(link);
    const activated = succeeded(visitor, HttpMethods.Post, (url) => url.includes(ApiPaths.Activate));
    await setPassword(visitor, chosenPassword);
    await activated;
    await visitor.waitForURL((url) => url.pathname.endsWith(AppPaths.Login) && url.search.includes(AppPaths.ActivatedNotice));
    return 'activated; the login page shows the activation notice';
  });

  await recorder.step('The user signs in with the new password', visitor, async () => {
    await signIn(visitor, recorder, { user: user.userName, password: chosenPassword });
    if ((await visitor.locator(Sel.LoginError).count()) > 0) throw new Error('The login page shows an error.');
    return `signed in as ${user.userName}; now at ${new URL(visitor.url()).pathname}`;
  });

  const second = await recorder.openPage(LANGUAGE);
  await recorder.step('Opening the same link again is refused', second, async () => {
    await second.goto(link);
    await setPassword(second, chosenPassword);
    await second.locator(Sel.ActivateError).first().waitFor();
    if (new URL(second.url()).pathname.endsWith(AppPaths.Login)) throw new Error('The reused link activated again.');
    return (await second.locator(Sel.ActivateError).first().innerText()).trim();
  });
}
