# Instructions for automated contributors

These instructions apply to the entire repository.

1. Before every change, read `docs/PROJECT_CORE.md` in full. Use its task-based routing matrix to read only the contracts, architecture, deviation, operations, security, legal, or development documents relevant to the requested task.
2. Read operator-facing `README.md` and operations guides only when changing configuration, deployment, releases, usage, or other externally visible behavior. A documentation task that directly edits one of those files must also read it.
3. Do not invent missing requirements, business rules, or user needs. State assumptions explicitly and request confirmation when they affect the outcome.
4. Follow the source-of-truth precedence in `docs/PROJECT_CORE.md`. Code and tests are evidence of current behavior and may be covered by a stable ID in `docs/DEVIATIONS.md`.
5. If code or tests conflict with confirmed intent and no registered deviation explains the conflict, report it. Do not silently change either side or broaden the task to resolve it.
6. Do not add functionality without a concrete request. Make the smallest necessary change and preserve unrelated user work.
7. Do not silently change architecture boundaries, data flow, side effects, component ownership, or failure behavior.
8. Update the relevant authoritative contract, architecture document, and deviation entry when their subject changes. Update operator-facing documentation only for externally visible changes.
9. Report uncertainties, areas not investigated, implementation deviations encountered, and tests not run.
10. Do not modify `Somtoday2MicrosoftSDS/OpenAPIs/openapi.json` or generated `openapi.cs` without an explicit request and a record of the source, specification version, and generation tool/version.
11. Never add secrets, tokens, connection strings, authentication bodies, personal data, production CSV files, or unsafe exception detail to code, tests, documentation, or logs.
12. Preserve cancellation for token acquisition, network requests, retries, SAS uploads, and validation polling. Follow `SECURITY.md` for security/privacy work and keep tracked configuration non-sensitive.
13. Write repository documentation and contributor instructions in English, except for `README.md`, which is deliberately maintained in Dutch for operators.
