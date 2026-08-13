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
14. In normative documentation, interpret and apply RFC 2119 key words according to RFC 8174 (only when they appear in all capitals). Write controlled technical prose in ASD-STE100 Simplified Technical English where practical.

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

When the user types `/graphify`, use the installed graphify skill or instructions before doing anything else.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- Dirty graphify-out/ files are expected after hooks or incremental updates; dirty graph files are not a reason to skip graphify. Only skip graphify if the task is about stale or incorrect graph output, or the user explicitly says not to use it.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
