// Flow 1 (phase-5 exit criterion): the admin edits a runtime setting, runs a job and sees its run history with the
// SYSTEM actor. The setting is reset to its default at the end, so the flow can run again.
import {
  ApiPaths,
  AppKeys,
  AppPaths,
  HttpMethods,
  MAX_SUCCESS_STATUS,
  Sel,
  SUCCEEDED_RUN_TONE,
  SystemActorLabels,
} from '../lib/flow-constants.mjs';
import { nextSettingValue } from '../lib/flow-checks.mjs';
import { rowWith, runJobAndWait, signIn } from '../lib/flow-runtime.mjs';

const LANGUAGE = 'en';

function savedSetting(page) {
  return page.waitForResponse(
    (response) =>
      response.request().method() === HttpMethods.Put &&
      response.url().includes(`${ApiPaths.AdminSettingsPrefix}/${AppKeys.Setting}`) &&
      response.status() <= MAX_SUCCESS_STATUS,
  );
}

export async function runSettingsJobs({ recorder, credentials }) {
  const page = await recorder.openPage(LANGUAGE);
  const row = rowWith(page, AppKeys.Setting);
  let next = null;
  let changed = false;

  await recorder.step('Admin signs in', page, async () => {
    await signIn(page, recorder, credentials);
    return `signed in as ${credentials.user}`;
  });

  await recorder.step(`Settings screen lists ${AppKeys.Setting}`, page, async () => {
    await page.goto(recorder.url(AppPaths.AdminSettings));
    await row.waitFor();
    const shown = await row.locator(Sel.SettingValue).innerText();
    next = nextSettingValue(shown);
    if (next === null) throw new Error(`The value "${shown}" is not a whole number.`);
    return `current value ${shown.trim()}`;
  });

  await recorder.step(`Admin changes it to ${next ?? 'the next value'} and saves`, page, async () => {
    await row.locator(Sel.SettingEdit).click();
    await page.locator(Sel.EditorValueInput).fill(next);
    const saved = savedSetting(page);
    await page.locator(Sel.OpenDialogConfirm).click();
    await saved;
    await page.waitForFunction(
      ({ rowSelector, key, valueSelector, value }) =>
        [...document.querySelectorAll(rowSelector)].some((tr) => tr.textContent.includes(key) && tr.querySelector(valueSelector)?.textContent.trim() === value),
      { rowSelector: Sel.ListRow, key: AppKeys.Setting, valueSelector: Sel.SettingValue, value: next },
    );
    changed = true;
    return `the list shows ${next}`;
  });

  await recorder.step(`Admin runs ${AppKeys.OutboxDrainJob} ("Run now") and its run history opens`, page, async () => {
    await runJobAndWait(page, recorder, AppKeys.OutboxDrainJob);
    return page.url();
  });

  await recorder.step('The newest run succeeded and ran as the system account', page, async () => {
    const newest = page.locator(Sel.ListRow).first();
    const tone = await newest.locator(`${Sel.RunStatus} ${Sel.StatusTone}`).getAttribute('data-tone');
    const text = await newest.innerText();
    if (tone !== SUCCEEDED_RUN_TONE) throw new Error(`The newest run has tone ${tone}, not ${SUCCEEDED_RUN_TONE}: ${text}`);
    if (!text.includes(SystemActorLabels[LANGUAGE])) throw new Error(`The newest run does not name the actor "${SystemActorLabels[LANGUAGE]}": ${text}`);
    return text.replace(/\s+/g, ' ').trim();
  });

  await recorder.cleanup('Clean-up: the setting is reset to its default', page, changed, async () => {
    await page.goto(recorder.url(AppPaths.AdminSettings));
    await row.locator(Sel.SettingEdit).click();
    await page.locator(Sel.EditorReset).click();
    const saved = savedSetting(page);
    await page.locator(Sel.OpenDialogConfirm).click();
    await saved;
    return 'reset';
  });
}
