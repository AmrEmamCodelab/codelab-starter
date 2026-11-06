# Scripted user flows

- App: http://localhost:4280
- Browser: 156.0.4314.58
- Started: 2026-11-06T17:28:37.830Z · finished: 2026-11-06T17:29:54.850Z
- Verdict: **FAIL**

| Flow | Result | Steps |
| --- | --- | --- |
| settings-jobs | passed | 6 |
| notification-bell | failed | 5 |
| activation | passed | 7 |
| contact-faq | passed | 8 |

## settings-jobs

| # | Step | Result | Detail | Screenshot |
| --- | --- | --- | --- | --- |
| 1 | Admin signs in | passed | signed in as breakglass | [settings-jobs-01.png](screenshots/settings-jobs-01.png) |
| 2 | Settings screen lists notifications.page-size | passed | current value 20 | [settings-jobs-02.png](screenshots/settings-jobs-02.png) |
| 3 | Admin changes it to 21 and saves | passed | the list shows 21 | [settings-jobs-03.png](screenshots/settings-jobs-03.png) |
| 4 | Admin runs outbox-drain ("Run now") and its run history opens | passed | http://localhost:4280/admin/jobs/outbox-drain/runs | [settings-jobs-04.png](screenshots/settings-jobs-04.png) |
| 5 | The newest run succeeded and ran as the system account | passed | Succeeded Manual System Administrator Nov 6, 2026, 7:28 PM Nov 6, 2026, 7:28 PM | [settings-jobs-05.png](screenshots/settings-jobs-05.png) |
| 6 | Clean-up: the setting is reset to its default | passed | reset | [settings-jobs-06.png](screenshots/settings-jobs-06.png) |

## notification-bell

| # | Step | Result | Detail | Screenshot |
| --- | --- | --- | --- | --- |
| 1 | Admin signs in (en) | passed | signed in as breakglass | [notification-bell-01.png](screenshots/notification-bell-01.png) |
| 2 | Admin sends a test notification from the notifications page | passed | accepted (202) | [notification-bell-02.png](screenshots/notification-bell-02.png) |
| 3 | The bell shows it with the English template wording | passed | the bell lists "Test notification" | [notification-bell-03.png](screenshots/notification-bell-03.png) |
| 4 | The bell shows it with the Arabic template wording | passed | the bell lists "إشعار تجريبي" | [notification-bell-04.png](screenshots/notification-bell-04.png) |
| 5 | Its e-mail is dropped in the pickup folder | failed | No e-mail in C:\Users\AMR\AppData\Local\Temp\starter-mail matching {"subjectIncludes":["إشعار تجريبي","Test notification"],"notBeforeMs":1793986120915} within 60000 ms. | [notification-bell-05.png](screenshots/notification-bell-05.png) |

## activation

| # | Step | Result | Detail | Screenshot |
| --- | --- | --- | --- | --- |
| 1 | Admin signs in | passed | signed in as breakglass | [activation-01.png](screenshots/activation-01.png) |
| 2 | Admin imports e2e-6t172836977z from an Excel sheet (preview) | passed | 1 rows will be created, 0 have errors. | [activation-02.png](screenshots/activation-02.png) |
| 3 | Admin commits the import | passed | committed | [activation-03.png](screenshots/activation-03.png) |
| 4 | The activation e-mail is dropped for the new user | passed | to e2e-6t172836977z@example.test: "تفعيل حسابك" with an activation link | [activation-04.png](screenshots/activation-04.png) |
| 5 | The user opens the link and sets a password | passed | activated; the login page shows the activation notice | [activation-05.png](screenshots/activation-05.png) |
| 6 | The user signs in with the new password | passed | signed in as e2e-6t172836977z; now at /unauthorized | [activation-06.png](screenshots/activation-06.png) |
| 7 | Opening the same link again is refused | passed | This activation link can no longer be used. Ask your administrator for a new one. | [activation-07.png](screenshots/activation-07.png) |

## contact-faq

| # | Step | Result | Detail | Screenshot |
| --- | --- | --- | --- | --- |
| 1 | A visitor submits the contact form | passed | accepted | [contact-faq-01.png](screenshots/contact-faq-01.png) |
| 2 | The page shows a CU-YYYY-NNN reference number of this year | passed | CU-2026-002 | [contact-faq-02.png](screenshots/contact-faq-02.png) |
| 3 | Admin signs in | passed | signed in as breakglass | [contact-faq-03.png](screenshots/contact-faq-03.png) |
| 4 | The contact inbox is notified (e-mail naming the reference number) | passed | to contact@example.invalid: "رسالة تواصل جديدة CU-2026-002" | [contact-faq-04.png](screenshots/contact-faq-04.png) |
| 5 | Admin edits the first FAQ title in ar and en and saves | passed | ar "E2E FAQ 6t172836977z عربي", en "E2E FAQ 6t172836977z English" | [contact-faq-05.png](screenshots/contact-faq-05.png) |
| 6 | The public FAQ shows the edit (ar) | passed | E2E FAQ 6t172836977z عربي | [contact-faq-06.png](screenshots/contact-faq-06.png) |
| 7 | The public FAQ shows the edit (en) | passed | E2E FAQ 6t172836977z English | [contact-faq-07.png](screenshots/contact-faq-07.png) |
| 8 | Clean-up: the FAQ titles are put back | passed | restored | [contact-faq-08.png](screenshots/contact-faq-08.png) |
