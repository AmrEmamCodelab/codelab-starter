import { Pipe, type PipeTransform } from '@angular/core';
import type { StatusWording, WorkflowAudience } from './records.models';
import { statusWordingKey } from './status-wording';

/**
 * `{{ record.wording | statusWording: audience | t }}`: the translation KEY of the record's status wording for the
 * audience (compose it with `t`). An absent wording (record not loaded yet) gives an empty string.
 */
@Pipe({ name: 'statusWording' })
export class StatusWordingPipe implements PipeTransform {
  transform(wording: StatusWording | null | undefined, audience: WorkflowAudience): string {
    return wording ? statusWordingKey(wording, audience) : '';
  }
}
