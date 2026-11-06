# Scripted user flows

- App: http://localhost:4280
- Browser: 156.0.4314.58
- Started: 2026-11-06T19:54:58.175Z · finished: 2026-11-06T19:55:05.140Z
- Verdict: **FAIL**

| Flow | Result | Steps |
| --- | --- | --- |
| attachments | failed | 7 |

## attachments

| # | Step | Result | Detail | Screenshot |
| --- | --- | --- | --- | --- |
| 1 | Admin signs in (en) and opens the attachments demo record | passed | signed in as breakglass; /attachments/demo shows its attachment type | [attachments-01.png](screenshots/attachments-01.png) |
| 2 | Admin uploads e2e-6t195457544z.pdf: browser -> attachment service (ticket header) -> API link | passed | service http://localhost:5185 took it (200, header x-starter-upload-ticket); API linked it (201) | [attachments-02.png](screenshots/attachments-02.png) |
| 3 | After a reload the record lists the PDF (read back from the API) | passed | e2e-6t195457544z.pdf is listed as uploaded | [attachments-03.png](screenshots/attachments-03.png) |
| 4 | A file over MaxFileBytes is refused with the English text and a reference code | passed | 10485761 bytes refused by the service (413): "The file is larger than the allowed size.", reference code 65C67-9D98F | [attachments-04.png](screenshots/attachments-04.png) |
| 5 | The same refusal shows the Arabic text and a reference code | passed | 10485761 bytes refused by the service (413): "حجم الملف أكبر من الحجم المسموح به.", reference code FDE94-10ECB | [attachments-05.png](screenshots/attachments-05.png) |
| 6 | Admin downloads it and gets the same bytes | failed | The download was refused: "The server could not be reached. Check your connection and try again." | [attachments-06.png](screenshots/attachments-06.png) |
| 7 | Admin removes e2e-6t195457544z.pdf from the demo record | passed | removed (204) | [attachments-07.png](screenshots/attachments-07.png) |
