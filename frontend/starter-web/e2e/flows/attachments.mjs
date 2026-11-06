// Flow 5 (phase-6 exit criterion): on /attachments/demo the admin uploads a PDF (browser -> attachment service with the
// ticket header, across origins, then the API links the receipt), sees it listed after a reload, downloads the same
// bytes, and a file over MaxFileBytes is refused with the bilingual text and a reference code in en and in ar. The
// refusals run before the download so one failure does not hide the other checks. The uploaded PDF is removed at the
// end (the demo type allows a few files only).
import { readFile } from 'node:fs/promises';
import {
  isCrossOrigin,
  judgeRefusal,
  oversizedPdf,
  oversizedPdfName,
  sameBytes,
  samplePdf,
  samplePdfName,
  ticketHeaderName,
} from '../lib/attachment-checks.mjs';
import {
  ApiPaths,
  ATTACHMENT_MAX_FILE_BYTES,
  AppPaths,
  AttachmentRowStatus,
  FileTooLargeTexts,
  HttpMethods,
  MAX_SUCCESS_STATUS,
  PDF_MIME,
  Sel,
} from '../lib/flow-constants.mjs';
import { signIn } from '../lib/flow-runtime.mjs';

const FIRST_LANGUAGE = 'en';
const SECOND_LANGUAGE = 'ar';

/** The demo record's first attachment type section (the demo record has one type). */
function typeSection(page) {
  return page.locator(Sel.AttachmentType).first();
}

function row(page, fileName) {
  return typeSection(page).locator(Sel.AttachmentRow).filter({ hasText: fileName });
}

async function openDemo(page, recorder) {
  await page.goto(recorder.url(AppPaths.AttachmentsDemo));
  await typeSection(page).waitFor();
}

/** The browser's upload to the attachment service: a POST that carries the ticket header. */
function serviceUpload(page) {
  return page.waitForRequest((request) => request.method() === HttpMethods.Post && ticketHeaderName(request.headers()) !== null);
}

function picks(page, fileName, buffer) {
  return typeSection(page).locator(Sel.AttachmentFileInput).setInputFiles({ name: fileName, mimeType: PDF_MIME, buffer });
}

async function waitForRowStatus(page, fileName, status) {
  await row(page, fileName).and(page.locator(`[${AttachmentRowStatus.Attribute}="${status}"]`)).first().waitFor();
}

async function refusesOversized(page, recorder, language, runStamp) {
  const fileName = oversizedPdfName(runStamp, language);
  const uploadSeen = serviceUpload(page);
  await picks(page, fileName, oversizedPdf(ATTACHMENT_MAX_FILE_BYTES));
  const request = await uploadSeen;
  await waitForRowStatus(page, fileName, AttachmentRowStatus.Failed);
  const refused = row(page, fileName).first();
  await page.locator(Sel.FeedbackMessage).first().waitFor();
  const verdict = judgeRefusal({
    rowErrorText: await refused.locator(Sel.AttachmentRowError).textContent(),
    rowReferenceText: await refused.locator(Sel.AttachmentRowReference).textContent().catch(() => null),
    bannerText: await page.locator(Sel.FeedbackMessage).first().textContent(),
    expectedText: FileTooLargeTexts[language],
  });
  if (!verdict.ok) throw new Error(`${language}: ${verdict.reason}`);
  const response = await request.response();
  return `${ATTACHMENT_MAX_FILE_BYTES + 1} bytes refused by the service (${response?.status() ?? 'no response'}): "${FileTooLargeTexts[language]}", reference code ${verdict.referenceCode}`;
}

export async function runAttachments({ recorder, credentials, options }) {
  const fileName = samplePdfName(options.runStamp);
  const content = samplePdf(fileName);
  const page = await recorder.openPage(FIRST_LANGUAGE);
  let uploaded = false;

  await recorder.step('Admin signs in (en) and opens the attachments demo record', page, async () => {
    await signIn(page, recorder, credentials);
    await openDemo(page, recorder);
    return `signed in as ${credentials.user}; ${AppPaths.AttachmentsDemo} shows its attachment type`;
  });

  await recorder.step(`Admin uploads ${fileName}: browser -> attachment service (ticket header) -> API link`, page, async () => {
    const uploadSeen = serviceUpload(page);
    const linked = page.waitForResponse(
      (response) =>
        response.request().method() === HttpMethods.Post &&
        response.url().includes(ApiPaths.RecordAttachmentsPrefix) &&
        response.url().endsWith(ApiPaths.AttachmentsSuffix),
    );
    await picks(page, fileName, content);
    const request = await uploadSeen;
    const serviceResponse = await request.response();
    if (!isCrossOrigin(request.url(), options.baseUrl)) throw new Error(`The upload went to ${request.url()}, not to the attachment service.`);
    if (!serviceResponse || serviceResponse.status() > MAX_SUCCESS_STATUS) throw new Error(`The attachment service answered ${serviceResponse?.status() ?? 'nothing'}.`);
    const link = await linked;
    if (link.status() > MAX_SUCCESS_STATUS) throw new Error(`The API refused the link: ${link.status()}.`);
    uploaded = true;
    await waitForRowStatus(page, fileName, AttachmentRowStatus.Uploaded);
    return `service ${new URL(request.url()).origin} took it (${serviceResponse.status()}, header ${ticketHeaderName(request.headers())}); API linked it (${link.status()})`;
  });

  await recorder.step('After a reload the record lists the PDF (read back from the API)', page, async () => {
    await openDemo(page, recorder);
    await waitForRowStatus(page, fileName, AttachmentRowStatus.Uploaded);
    return `${fileName} is listed as uploaded`;
  });

  await recorder.step('A file over MaxFileBytes is refused with the English text and a reference code', page, () =>
    refusesOversized(page, recorder, FIRST_LANGUAGE, options.runStamp),
  );

  const arabic = await recorder.openPage(SECOND_LANGUAGE);
  await recorder.step('The same refusal shows the Arabic text and a reference code', arabic, async () => {
    await signIn(arabic, recorder, credentials);
    await openDemo(arabic, recorder);
    return refusesOversized(arabic, recorder, SECOND_LANGUAGE, options.runStamp);
  });

  await recorder.step('Admin downloads it and gets the same bytes', page, async () => {
    await openDemo(page, recorder);
    const downloadSeen = page.waitForEvent('download');
    const refusalSeen = page.locator(Sel.FeedbackMessage).first().waitFor().then(() => null);
    await row(page, fileName).first().locator(Sel.AttachmentDownload).click();
    // The losing wait times out later; mark both handled so that rejection is not an unhandled one.
    for (const wait of [downloadSeen, refusalSeen]) wait.catch(() => undefined);
    const download = await Promise.race([downloadSeen, refusalSeen]);
    if (download === null) {
      throw new Error(`The download was refused: "${(await page.locator(Sel.FeedbackMessage).first().textContent())?.trim()}"`);
    }
    const saved = await readFile(await download.path());
    if (!sameBytes(saved, content)) throw new Error(`The download has ${saved.length} bytes that differ from the ${content.length} uploaded.`);
    return `"${download.suggestedFilename()}", ${saved.length} bytes, identical to the upload`;
  });

  await recorder.cleanup(`Admin removes ${fileName} from the demo record`, page, uploaded, async () => {
    await openDemo(page, recorder);
    const removed = page.waitForResponse(
      (response) => response.request().method() === HttpMethods.Delete && response.url().includes(ApiPaths.AttachmentPrefix),
    );
    await row(page, fileName).first().locator(Sel.AttachmentRemove).click();
    const response = await removed;
    if (response.status() > MAX_SUCCESS_STATUS) throw new Error(`The API refused the removal: ${response.status()}.`);
    await row(page, fileName).first().waitFor({ state: 'detached' });
    return `removed (${response.status()})`;
  });
}
