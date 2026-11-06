# Scripted user flows

- App: http://localhost:4380
- Browser: 156.0.4314.58
- Started: 2026-11-06T22:45:00.736Z · finished: 2026-11-06T22:45:35.569Z
- Verdict: **FAIL**

| Flow | Result | Steps |
| --- | --- | --- |
| service-requests | failed | 17 |

## service-requests

| # | Step | Result | Detail | Screenshot |
| --- | --- | --- | --- | --- |
| 1 | Set-up: the admin grants dev.viewer the requester keys for the run | passed | granted service-requests.create, service-requests.view-own, attachments.view, attachments.upload | [service-requests-01.png](screenshots/service-requests-01.png) |
| 2 | [ar-1440] Requester and reviewer sign in | passed | requester dev.viewer, reviewer breakglass | [service-requests-02.png](screenshots/service-requests-02.png) |
| 3 | [ar-1440] 1a. Requester fills the 3-step wizard, attaches a PDF and saves the draft | failed | locator.setInputFiles: Timeout 30000ms exceeded. | [service-requests-03.png](screenshots/service-requests-03.png) |
| 4 | [ar-1440] 1b. Requester resumes the draft from My requests and submits it | skipped | an earlier step failed |  |
| 5 | [ar-1440] 2. Reviewer sees it in the queue, claims it and starts the review | skipped | an earlier step failed |  |
| 6 | [ar-1440] 3. Requester sees the status update and a new notification | skipped | an earlier step failed |  |
| 7 | [ar-1440] 4a. The return dialog blocks an empty reason | skipped | an earlier step failed |  |
| 8 | [ar-1440] 4b. Reviewer returns it with a reason | skipped | an earlier step failed |  |
| 9 | [ar-1440] 5. Requester sees the reason and resubmits | skipped | an earlier step failed |  |
| 10 | [ar-1440] 6. Reviewer approves; the request is terminal with no further actions | skipped | an earlier step failed |  |
| 11 | [ar-1440] 7a. The timeline shows every step with its actor | skipped | an earlier step failed |  |
| 12 | [ar-1440] 7b. The admin sees it read-only in the oversight | skipped | an earlier step failed |  |
| 13 | [ar-1440] 8a. Requester files a second request | skipped | an earlier step failed |  |
| 14 | [ar-1440] 8b. Reviewer rejects it with a reason; it is terminal | skipped | an earlier step failed |  |
| 15 | [ar-1440] 8c. Requester sees the rejection reason and has no resubmit action | skipped | an earlier step failed |  |
| 16 | Summary: the record numbers this run produced | skipped | an earlier step failed |  |
| 17 | Clean-up: the requester keys granted for the run are taken back | passed | revoked service-requests.create, service-requests.view-own, attachments.view, attachments.upload | [service-requests-17.png](screenshots/service-requests-17.png) |
