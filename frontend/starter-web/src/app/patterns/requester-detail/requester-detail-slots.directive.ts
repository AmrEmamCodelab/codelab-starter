import { Directive } from '@angular/core';

/**
 * Marks the projected attachments content (P6 wires the attachment list and upload): `<div detailAttachments>`. When
 * present, the page renders an "Attachments" section around it; without it there is no empty section.
 */
@Directive({ selector: '[detailAttachments]' })
export class RequesterDetailAttachmentsDirective {}

/**
 * Marks the projected actions (cancel, resubmit...): `<div detailActions>`. The screen gates each action with
 * `*hasPermission` and enables it by status; when present, the page renders a labelled action group beside the tracker.
 */
@Directive({ selector: '[detailActions]' })
export class RequesterDetailActionsDirective {}
