/** Every text of the jobs screens (`admin.jobs.*`), in `ar.json` and `en.json`. */
export const JobsTranslationKeys = {
  Title: 'admin.jobs.title',
  Intro: 'admin.jobs.intro',
  Empty: 'admin.jobs.empty',
  Columns: {
    Name: 'admin.jobs.columns.name',
    Schedule: 'admin.jobs.columns.schedule',
    LastRun: 'admin.jobs.columns.last-run',
  },
  Schedule: {
    ManualOnly: 'admin.jobs.schedule.manual-only',
  },
  LastRun: {
    Never: 'admin.jobs.last-run.never',
  },
  Actions: {
    RunNow: 'admin.jobs.actions.run-now',
    History: 'admin.jobs.actions.history',
  },
  Run: {
    Title: 'admin.jobs.run.title',
    Text: 'admin.jobs.run.text',
    Confirm: 'admin.jobs.run.confirm',
  },
  Status: {
    Queued: 'admin.jobs.status.queued',
    Running: 'admin.jobs.status.running',
    Succeeded: 'admin.jobs.status.succeeded',
    Failed: 'admin.jobs.status.failed',
  },
  Triggers: {
    Schedule: 'admin.jobs.triggers.schedule',
    Manual: 'admin.jobs.triggers.manual',
  },
  Runs: {
    Title: 'admin.jobs.runs.title',
    Job: 'admin.jobs.runs.job',
    Empty: 'admin.jobs.runs.empty',
    Back: 'admin.jobs.runs.back',
    Refresh: 'admin.jobs.runs.refresh',
    ActorSystem: 'admin.jobs.runs.actor-system',
    NotYet: 'admin.jobs.runs.not-yet',
    NoUser: 'admin.jobs.runs.no-user',
    Columns: {
      Status: 'admin.jobs.runs.columns.status',
      Trigger: 'admin.jobs.runs.columns.trigger',
      Actor: 'admin.jobs.runs.columns.actor',
      TriggeredBy: 'admin.jobs.runs.columns.triggered-by',
      StartedAt: 'admin.jobs.runs.columns.started-at',
      FinishedAt: 'admin.jobs.runs.columns.finished-at',
      ReferenceCode: 'admin.jobs.runs.columns.reference-code',
    },
  },
} as const;
