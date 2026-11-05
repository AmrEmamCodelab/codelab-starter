// Lint fixture: core/api is the one place HTTP primitives are allowed (base.fe-http-via-api-service).
import type { HttpClient } from '@angular/common/http';

export class ApiService {
  constructor(private readonly http: HttpClient) {}

  client(): HttpClient {
    return this.http;
  }
}
