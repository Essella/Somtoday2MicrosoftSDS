# Security policy

## Supported versions and reporting

Only the latest release receives security fixes. Report suspected vulnerabilities privately with GitHub's **Report a vulnerability** feature. Do not include credentials or identifiable pupil, guardian, or employee data.

## Secrets and personal data

- Never commit Somtoday secrets, Azure tokens, SAS URLs or query strings, authentication bodies, or production CSV data.
- Supply `somtodayClientSecret` only through the secure deployment parameter. It becomes a Container Apps Job secret exposed as `Somtoday__ClientSecret`.
- Production Graph access is constrained to the Job's system-assigned managed identity. The Cloud Shell role-assignment script grants only `IndustryData-InboundFlow.ReadWrite.All`, `IndustryData-DataConnector.Upload`, and the validation-operation polling permission `IndustryData.ReadBasic.All` to tagged Jobs in tagged resource groups.
- The Graph bearer token is sent only to `graph.microsoft.com`. SAS uploads use a separate `HttpClient` and never receive an Authorization header.
- Treat the upload-session URL as a secret. Preserve its query string for upload but never log it, a filename URL containing it, or response bodies that may expose it.
- CSV data is built in memory and persists only in the temporary SDS-owned SAS container. This repository provisions no application Storage Account.
- Guardian-name exclusion logs contain only a count. CSV CR/LF errors contain only SDS version, file name, and column name.
- Preserve cancellation through token acquisition, Graph calls, retries, SAS uploads, and validation polling.
- Somtoday authentication, public discovery, and authenticated data clients do not follow redirects. This keeps the authentication form and school-data requests on their configured endpoints.
- Treat the Somtoday client secret as opaque. Reject a whitespace-only value, but do not trim or normalize any other value.
- NIGHTLY uses plaintext HTTP and is Development-only; never use it with real personal data.
- Treat Dynamic LINQ username expressions as trusted administrator code and do not use sensitive model fields.

Rotate any credential that may have entered history, logs, an issue, or an artifact. Removing it from the latest commit is insufficient. The deploying organization remains responsible for tenant access controls, monitoring, purpose limitation, retention, and AVG/GDPR compliance.
