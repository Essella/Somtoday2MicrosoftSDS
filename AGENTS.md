# Instructions for automated contributors

These instructions apply to the entire repository.

1. Read `README.md` and `docs/PROJECT_CONTEXT.md` in full before every change.
2. Do not invent missing requirements, business rules, or user needs. State assumptions explicitly and request confirmation when they affect the outcome.
3. Treat `docs/PROJECT_CONTEXT.md` as the leading source of truth for confirmed intent. Treat the implementation and tests as evidence of current behavior, which may deviate from that intent.
4. If code or tests conflict with confirmed intent in `docs/PROJECT_CONTEXT.md`, report the deviation explicitly. Do not silently change either side or broaden the requested work to resolve it.
5. Do not add functionality without a concrete request. Make the smallest necessary change.
6. Do not silently change architecture boundaries, data flows, side effects, or component responsibilities.
7. Update the relevant documentation when behavior, component responsibilities, invariants, or boundaries change.
8. Report uncertainties, areas that were not investigated, and tests that were not run.
9. Do not modify `Somtoday2MicrosoftSDS/OpenAPIs/openapi.json` or the generated `openapi.cs` without an explicit request and a record of the source, specification version, and generation tool/version.
10. Never add secrets, tokens, connection strings, personal data, or production CSV files to code, tests, documentation, or logs.
11. Write repository documentation and contributor instructions in English, except for `README.md`, which is deliberately maintained in Dutch for operators.
