import { bootstrapApplication } from '@angular/platform-browser';
import { App } from './app/app';
import { createAppConfig } from './app/app.config';
import { loadRuntimeConfig } from './app/core/config/runtime-config.loader';

// config.json is read before bootstrap: the providers need its signing prefix and API roots. A missing
// or malformed file stops the app here, loudly, instead of letting it talk to the wrong API.
loadRuntimeConfig()
  .then(config => bootstrapApplication(App, createAppConfig(config)))
  .catch((error: unknown) => console.error(error));
