import { bootstrapApplication } from '@angular/platform-browser';
import { App } from './app/app';
import { createAppConfig } from './app/app.config';
import { loadRuntimeConfig } from './app/core/config/runtime-config.loader';

// config.json is read (relative to the base href) and checked against the served base href before bootstrap. A
// missing, malformed or mismatched file stops the console here, loudly.
loadRuntimeConfig(document.baseURI)
  .then((config) => bootstrapApplication(App, createAppConfig(config)))
  .catch((error: unknown) => console.error(error));
