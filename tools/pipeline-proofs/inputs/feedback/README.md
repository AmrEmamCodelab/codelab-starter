# Feedback proof input (placeholder)

The third pipeline proof (Figma frame to a "Feedback" screen) needs **one frame in a Codelab Figma file**. The
platform owner provides it; nothing calls Figma until then.

| Item | State |
| --- | --- |
| Figma file and frame | open: the owner provides the frame URL (`https://www.figma.com/design/<fileKey>/<name>?node-id=<id>`) |
| Access for the run | open: the headless run needs the Figma MCP connector signed in to an account that can open the file |
| Scenario | `scenario.txt` (plain business wording, no slash command) |
| Definition | `proof.json` (`requiresFigmaUrl: true`) |

## Run it once the URL exists

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\pipeline-proofs\Invoke-PipelineProof.ps1 -Proof feedback `
  -FigmaUrl "<frame url>" -EvidenceDir docs\evidence\P9\<folder> -PreApproveSpec
```

Without `-FigmaUrl` the harness reports the proof as `blocked` (exit code 2) and starts nothing, in a dry run too.
The frame URL is passed in the prompt only; it is never committed here.
