// Flow 6 (phase-7 exit criterion, steps 1-8): the Service Request sample end to end, once per language (ar, en) at the
// desktop width and at a phone width. The requester is the first Development user (granted the requester keys for the
// run and stripped of them at the end); the reviewer and the admin oversight are the break-glass administrator, a
// separate account because the engine refuses the staff rules to the record's owner. Step 9 (an SLA breach raises an
// escalation) needs an advanced TimeProvider and is proven in the test host (SlaBreachEscalationTests), not here.
import { readFileSync } from 'node:fs';
import { setTimeout as delay } from 'node:timers/promises';
import { samplePdf, samplePdfName } from '../lib/attachment-checks.mjs';
import { AppKeys, HttpMethods, MAX_SUCCESS_STATUS, PDF_MIME, Sel } from '../lib/flow-constants.mjs';
import { runJobAndWait, signIn } from '../lib/flow-runtime.mjs';
import {
  actorSummary,
  flowVariants,
  recordNumberOf,
  requestTitle,
  requesterCredentials,
  timelineCovers,
} from '../lib/service-request-checks.mjs';
import {
  APPROVED_PATH_ACTIONS,
  MIN_TIMELINE_ACTORS,
  RequesterGrantKeys,
  SrActions,
  SrApi,
  SrData,
  SrPaths,
  SrSel,
  SrTimings,
  SrViewports,
} from '../lib/service-request-constants.mjs';

const SETUP_LANGUAGE = 'en';
const FIRST_REQUEST = 1;
const SECOND_REQUEST = 2;

function succeeded(page, method, matches) {
  return page.waitForResponse((response) => response.request().method() === method && matches(response.url()) && response.status() <= MAX_SUCCESS_STATUS);
}

function transitioned(page) {
  return succeeded(page, HttpMethods.Post, (url) => url.endsWith(SrApi.TransitionsSuffix));
}

function listRow(page, text) {
  return page.locator(Sel.ListRow).filter({ hasText: text });
}

// ---- set-up: the requester's grants ----

async function openRequesterGrants(admin, recorder, userName) {
  await admin.goto(recorder.url(SrPaths.AdminUsers));
  await listRow(admin, userName).locator(SrSel.UserGrantsLink).click();
  await admin.locator(SrSel.GrantsSave).waitFor();
}

function grantBox(admin, key) {
  return admin.locator(`${SrSel.GrantItem}[data-key="${key}"]`).locator(SrSel.GrantCheckbox).first();
}

/** Sets each key to `checked` and saves; returns the keys it changed. */
async function setGrants(admin, keys, checked) {
  const changed = [];
  for (const key of keys) {
    const box = grantBox(admin, key);
    if ((await box.isChecked()) !== checked) {
      await box.setChecked(checked);
      changed.push(key);
    }
  }
  if (changed.length > 0) {
    const saved = succeeded(admin, HttpMethods.Put, (url) => url.includes(SrApi.GrantsSuffix));
    await admin.locator(SrSel.GrantsSave).click();
    await saved;
  }
  return changed;
}

// ---- requester ----

async function fillFirstSteps(page, title) {
  await page.locator(SrSel.CategorySelect).selectOption({ index: SrData.CategoryOptionIndex });
  await page.locator(SrSel.TitleInput).fill(title);
  await page.locator(SrSel.WizardNext).click();
  await page.locator(SrSel.DescriptionInput).waitFor();
  await page.locator(SrSel.DescriptionInput).fill(SrData.Description);
  await page.locator(SrSel.PhoneInput).fill(SrData.Phone);
  await page.locator(SrSel.WizardNext).click();
  await page.locator(SrSel.WizardReview).waitFor();
}

/** Uploads the sample PDF into the first (required) attachment type of the record. */
async function attachDocument(page, fileName) {
  const section = page.locator(SrSel.AttachmentType).first();
  await section.locator(SrSel.AttachmentFileInput).setInputFiles({ name: fileName, mimeType: PDF_MIME, buffer: samplePdf(fileName) });
  await section.locator(SrSel.AttachmentUploadedRow).filter({ hasText: fileName }).waitFor();
}

async function submitWizard(page, year) {
  const submitted = succeeded(page, HttpMethods.Post, (url) => url.endsWith(SrApi.SubmitSuffix));
  await page.locator(SrSel.WizardSubmit).click();
  await submitted;
  const shown = await page.locator(SrSel.WizardNumber).innerText();
  const number = recordNumberOf(shown, year);
  if (number === null) throw new Error(`The wizard shows "${shown}", not an SR-${year}-NNN number.`);
  return number;
}

async function openOwnRequest(page, recorder, text) {
  await page.goto(recorder.url(SrPaths.Mine));
  await listRow(page, text).locator(SrSel.MineOpen).click();
}

async function timelineActions(page) {
  return page.locator(SrSel.TimelineEntry).evaluateAll((entries) => entries.map((entry) => entry.getAttribute('data-action') ?? ''));
}

async function waitForTimelineAction(page, action) {
  await page.locator(`${SrSel.TimelineEntry}[data-action="${action}"]`).first().waitFor();
}

async function feedCount(page, recorder, number) {
  await page.goto(recorder.url(SrPaths.Notifications));
  await page.locator(Sel.NotificationsTitle).waitFor();
  await delay(SrTimings.NotificationPollMs);
  return page.locator(SrSel.FeedItemTitle).filter({ hasText: number }).count();
}

/** Waits until the requester's feed holds more items naming the number than before; drains the outbox once if late. */
async function waitForNewNotification({ page, recorder, number, before, admin }) {
  const deadline = Date.now() + SrTimings.NotificationWaitMs;
  let drained = false;
  while (Date.now() < deadline) {
    const now = await feedCount(page, recorder, number);
    if (now > before) return now;
    if (!drained) {
      await runJobAndWait(admin, recorder, AppKeys.OutboxDrainJob);
      drained = true;
    }
  }
  throw new Error(`No new notification naming ${number} within ${SrTimings.NotificationWaitMs} ms (had ${before}).`);
}

// ---- staff ----

async function claimFromQueue(staff, recorder, number) {
  await staff.goto(recorder.url(SrPaths.StaffQueue));
  const row = listRow(staff, number);
  await row.waitFor();
  const claimed = succeeded(staff, HttpMethods.Post, (url) => url.endsWith(SrApi.ClaimSuffix));
  await row.locator(SrSel.QueueClaim).click();
  await claimed;
  await row.locator(SrSel.QueueAssignee).waitFor();
  await row.locator(SrSel.QueueOpen).click();
  await staff.locator(SrSel.StaffStatus).waitFor();
}

function staffAction(staff, action) {
  return staff.locator(`${SrSel.StaffActions} [data-action="${action}"] button`);
}

/** Runs one staff decision; a reason dialog (required or optional reason) is filled with `reason` and confirmed. */
async function decide(staff, action, reason) {
  const done = transitioned(staff);
  await staffAction(staff, action).click();
  const dialog = staff.locator(SrSel.ReasonDialogInput);
  const first = await Promise.race([
    done.then(() => 'done'),
    dialog.waitFor().then(() => 'dialog'),
  ]);
  if (first === 'dialog') {
    if (reason) await dialog.fill(reason);
    await staff.locator(SrSel.ReasonDialogConfirm).click();
  }
  await done;
  await staff.locator(SrSel.StaffDecided).waitFor();
}

/** Opens the reason dialog of a reason-required action and proves an empty reason cannot be confirmed. */
async function emptyReasonBlocked(staff, action) {
  await staffAction(staff, action).click();
  await staff.locator(SrSel.ReasonDialogInput).waitFor();
  const confirm = staff.locator(SrSel.ReasonDialogConfirm);
  if (await confirm.isEnabled()) {
    let posted = false;
    const listener = (request) => {
      if (request.method() === HttpMethods.Post && request.url().endsWith(SrApi.TransitionsSuffix)) posted = true;
    };
    staff.on('request', listener);
    await confirm.click();
    await delay(SrTimings.NotificationPollMs);
    staff.off('request', listener);
    if (posted) throw new Error(`An empty reason was sent for ${action}.`);
    if (!(await staff.locator(SrSel.ReasonDialogInput).isVisible())) throw new Error(`The ${action} dialog closed with an empty reason.`);
    return 'confirm with an empty reason keeps the dialog open and sends nothing';
  }
  return 'confirm is disabled while the reason is empty';
}

async function assertNoActions(page, scope) {
  const count = await page.locator(`${scope} ${SrSel.AnyActionButton}`).count();
  if (count > 0) throw new Error(`${count} action(s) still offered in ${scope}.`);
}

// ---- one run (language x viewport) ----

async function runVariant({ recorder, credentials, requester, variant, runStamp, year, admin }) {
  const { language, viewport, label } = variant;
  const tag = `[${label}]`;
  const title = requestTitle(runStamp, label, FIRST_REQUEST);
  const secondTitle = requestTitle(runStamp, label, SECOND_REQUEST);
  const fileName = samplePdfName(`${runStamp}-${label}`);
  const page = await recorder.openPage(language, viewport);
  const staff = await recorder.openPage(language, viewport);
  let number = null;
  let secondNumber = null;
  let notificationsBefore = 0;

  await recorder.step(`${tag} Requester and reviewer sign in`, page, async () => {
    await signIn(page, recorder, requester);
    await signIn(staff, recorder, credentials);
    return `requester ${requester.user}, reviewer ${credentials.user}`;
  });

  await recorder.step(`${tag} 1a. Requester fills the 3-step wizard, attaches a PDF and saves the draft`, page, async () => {
    await page.goto(recorder.url(SrPaths.New));
    await fillFirstSteps(page, title);
    await attachDocument(page, fileName);
    const saved = succeeded(page, HttpMethods.Put, (url) => url.endsWith(SrApi.DraftSuffix));
    await page.locator(SrSel.WizardDraft).click();
    await saved;
    return `draft "${title}" saved with ${fileName}`;
  });

  await recorder.step(`${tag} 1b. Requester resumes the draft from My requests and submits it`, page, async () => {
    await openOwnRequest(page, recorder, title);
    await page.locator(SrSel.TitleInput).waitFor();
    const restored = await page.locator(SrSel.TitleInput).inputValue();
    if (restored !== title) throw new Error(`The resumed draft shows the title "${restored}".`);
    await page.locator(SrSel.WizardNext).click();
    await page.locator(SrSel.DescriptionInput).waitFor();
    await page.locator(SrSel.WizardNext).click();
    await page.locator(SrSel.AttachmentUploadedRow).filter({ hasText: fileName }).waitFor();
    number = await submitWizard(page, year);
    notificationsBefore = await feedCount(page, recorder, number);
    return `submitted as ${number}`;
  });

  await recorder.step(`${tag} 2. Reviewer sees it in the queue, claims it and starts the review`, staff, async () => {
    await claimFromQueue(staff, recorder, number);
    await decide(staff, SrActions.StartReview, null);
    await staff.locator(SrSel.SlaBadge).first().waitFor();
    return `${number} claimed and under review: ${(await staff.locator(SrSel.StaffStatus).innerText()).trim()} (SLA badge shown)`;
  });

  await recorder.step(`${tag} 3. Requester sees the status update and a new notification`, page, async () => {
    const count = await waitForNewNotification({ page, recorder, number, before: notificationsBefore, admin });
    await openOwnRequest(page, recorder, number);
    await waitForTimelineAction(page, SrActions.StartReview);
    notificationsBefore = count;
    return `${count} notification(s) name ${number}; the tracker and timeline show the review`;
  });

  await recorder.step(`${tag} 4a. The return dialog blocks an empty reason`, staff, async () => {
    await staff.reload();
    await staff.locator(SrSel.StaffStatus).waitFor();
    return emptyReasonBlocked(staff, SrActions.Return);
  });

  await recorder.step(`${tag} 4b. Reviewer returns it with a reason`, staff, async () => {
    const done = transitioned(staff);
    await staff.locator(SrSel.ReasonDialogInput).fill(SrData.ReturnReason);
    await staff.locator(SrSel.ReasonDialogConfirm).click();
    await done;
    await staff.locator(SrSel.StaffDecided).waitFor();
    return `returned: "${SrData.ReturnReason}"`;
  });

  await recorder.step(`${tag} 5. Requester sees the reason and resubmits`, page, async () => {
    await openOwnRequest(page, recorder, number);
    const reason = (await page.locator(SrSel.DetailReason).innerText()).trim();
    if (!reason.includes(SrData.ReturnReason)) throw new Error(`The detail shows the reason "${reason}".`);
    const done = transitioned(page);
    await page.locator(`${SrSel.DetailActions} [data-action="${SrActions.Resubmit}"] button`).click();
    const first = await Promise.race([done.then(() => 'done'), page.locator(SrSel.ConfirmDialogConfirm).waitFor().then(() => 'dialog')]);
    if (first === 'dialog') await page.locator(SrSel.ConfirmDialogConfirm).click();
    await done;
    await waitForTimelineAction(page, SrActions.Resubmit);
    return `reason shown ("${reason}"); resubmitted`;
  });

  await recorder.step(`${tag} 6. Reviewer approves; the request is terminal with no further actions`, staff, async () => {
    await staff.reload();
    await staff.locator(SrSel.StaffStatus).waitFor();
    await decide(staff, SrActions.StartReview, null);
    await staff.reload();
    await staff.locator(SrSel.StaffStatus).waitFor();
    await decide(staff, SrActions.Approve, SrData.ApproveNote);
    await staff.reload();
    await staff.locator(SrSel.StaffNoActions).waitFor();
    await assertNoActions(staff, SrSel.StaffActions);
    await openOwnRequest(page, recorder, number);
    await waitForTimelineAction(page, SrActions.Approve);
    await assertNoActions(page, SrSel.DetailActions);
    return `${number}: ${(await staff.locator(SrSel.StaffStatus).innerText()).trim()}; no action for the reviewer or the requester`;
  });

  await recorder.step(`${tag} 7a. The timeline shows every step with its actor`, staff, async () => {
    const actions = await timelineActions(staff);
    const covered = timelineCovers(actions, APPROVED_PATH_ACTIONS);
    if (!covered.ok) throw new Error(`The timeline [${actions.join(', ')}] misses ${covered.missing.join(', ')}.`);
    const actors = actorSummary(await staff.locator(SrSel.TimelineActor).allInnerTexts(), MIN_TIMELINE_ACTORS);
    if (!actors.ok) throw new Error(`Timeline actors: ${actors.blank} blank, distinct [${actors.distinct.join(' | ')}].`);
    return `${actions.length} entries [${actions.join(', ')}]; actors ${actors.distinct.join(' | ')}`;
  });

  await recorder.step(`${tag} 7b. The admin sees it read-only in the oversight`, staff, async () => {
    await staff.goto(recorder.url(SrPaths.AdminOversight));
    await listRow(staff, number).locator(SrSel.OversightOpen).click();
    await staff.locator(SrSel.OversightReadOnly).first().waitFor();
    await staff.locator(SrSel.TimelineEntry).first().waitFor();
    await assertNoActions(staff, 'main');
    return `${number} open in the oversight, read-only, ${await staff.locator(SrSel.TimelineEntry).count()} timeline entries, no action`;
  });

  await recorder.step(`${tag} 8a. Requester files a second request`, page, async () => {
    await page.goto(recorder.url(SrPaths.New));
    await fillFirstSteps(page, secondTitle);
    await attachDocument(page, fileName);
    secondNumber = await submitWizard(page, year);
    return `submitted as ${secondNumber}`;
  });

  await recorder.step(`${tag} 8b. Reviewer rejects it with a reason; it is terminal`, staff, async () => {
    await claimFromQueue(staff, recorder, secondNumber);
    await decide(staff, SrActions.StartReview, null);
    await staff.reload();
    await staff.locator(SrSel.StaffStatus).waitFor();
    await decide(staff, SrActions.Reject, SrData.RejectReason);
    await staff.reload();
    await staff.locator(SrSel.StaffNoActions).waitFor();
    await assertNoActions(staff, SrSel.StaffActions);
    return `${secondNumber}: ${(await staff.locator(SrSel.StaffStatus).innerText()).trim()}`;
  });

  await recorder.step(`${tag} 8c. Requester sees the rejection reason and has no resubmit action`, page, async () => {
    await openOwnRequest(page, recorder, secondNumber);
    await waitForTimelineAction(page, SrActions.Reject);
    const reason = (await page.locator(SrSel.DetailReason).innerText()).trim();
    if (!reason.includes(SrData.RejectReason)) throw new Error(`The detail shows the reason "${reason}".`);
    await assertNoActions(page, SrSel.DetailActions);
    return `reason shown ("${reason}"); no resubmit or other action`;
  });

  return { number, secondNumber };
}

export async function runServiceRequests({ recorder, credentials, options }) {
  const requester = requesterCredentials(readFileSync(options.secretsFile, 'utf8'));
  if (requester.error) throw new Error(requester.error);
  recorder.secrets.push(requester.password);
  const year = new Date().getFullYear();
  const admin = await recorder.openPage(SETUP_LANGUAGE, SrViewports.Desktop);
  let granted = [];

  await recorder.step(`Set-up: the admin grants ${requester.user} the requester keys for the run`, admin, async () => {
    await signIn(admin, recorder, credentials);
    await openRequesterGrants(admin, recorder, requester.user);
    granted = await setGrants(admin, RequesterGrantKeys, true);
    return granted.length > 0 ? `granted ${granted.join(', ')}` : `already held ${RequesterGrantKeys.join(', ')}`;
  });

  const numbers = [];
  for (const variant of flowVariants()) {
    if (recorder.failed) break;
    numbers.push(await runVariant({ recorder, credentials, requester, variant, runStamp: options.runStamp, year, admin }));
  }

  await recorder.step('Summary: the record numbers this run produced', null, async () =>
    numbers.map(({ number, secondNumber }) => `${number} (approved), ${secondNumber} (rejected)`).join('; '),
  );

  await recorder.cleanup(`Clean-up: the requester keys granted for the run are taken back`, admin, granted.length > 0, async () => {
    await openRequesterGrants(admin, recorder, requester.user);
    const revoked = await setGrants(admin, granted, false);
    return `revoked ${revoked.join(', ')}`;
  });
}
