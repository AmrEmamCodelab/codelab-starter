// Lint fixture: a clean file. Explicit integer enum values (including -1), a numeric literal type, numbers held by
// const declarations, an allowed number (0, 1, -1), an array index and a default value pass.
export enum InvoiceStatus {
  Unknown = -1,
  Draft = 1,
  Sent = 2,
  Paid = 30,
}

export type PageSize = 10 | 25;

export const DAYS_TO_PAY = 14;
export const FIRST_REMINDER_DAY = 3;
export const SECOND_REMINDER_DAY = 7;
export const REMINDER_DAYS = [FIRST_REMINDER_DAY, SECOND_REMINDER_DAY] as const;

export function firstReminder(days: readonly number[] = REMINDER_DAYS, step = 2): number {
  return days.length > 0 ? days[0] * step : -1;
}
