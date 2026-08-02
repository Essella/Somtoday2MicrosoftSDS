# Security policy

## Supported versions

Only the latest published release receives security fixes. Pin production deployments to a released version or digest and update after reviewing each release.

## Report a vulnerability

Do not create a public issue for a suspected vulnerability or an exposed credential. Use GitHub's **Report a vulnerability** option on the Security tab of this repository to submit a private report. Include the affected version, impact, reproduction steps and any proposed mitigation. Do not include working credentials or identifiable pupil, guardian or employee data.

The maintainer will acknowledge a usable report, investigate it and coordinate disclosure. No fixed response or remediation time is guaranteed.

## Secrets and personal data

- Never commit Somtoday secrets, storage connection strings, Azure tokens or production CSV data.
- In Azure, bootstrap `Somtoday__ClientSecret` only long enough to store it in Key Vault. Redeploy without the parameter after a successful run.
- Rotate a credential immediately if it may have appeared in Git history, logs, an issue or a build artifact. Removing it from the latest commit is not sufficient.
- NIGHTLY has a plaintext HTTP Somtoday data endpoint and is restricted to Development. Never use it with real personal data or deploy it as a production environment.
- Staging at `{Output:Folder}/.staging/{RunId}/{FileName}` is temporary data for only the current run and dataset; never use it for rollback, later-run recovery, or Power Automate ingestion. Cleanup is retried four times and remains best effort, so monitor warnings. Infrastructure makes staging base Blobs and versions lifecycle-eligible after more than one day, but asynchronous lifecycle processing and seven-day soft delete can retain the data longer.
- Do not run overlapping instances. Startup cleanup deliberately recognizes both current and legacy application staging and can remove another run's files.
- Guardian-name exclusion logging contains only a count. CSV CR/LF validation errors contain only SDS version, file name, and column name. Do not add source names, UUIDs, email addresses, phone numbers, field values, or CSV rows to either message.
- Treat Dynamic LINQ username expressions as trusted administrator code, not a sandbox. Do not use BSN/ECK identifiers, phones, dates, nested objects, or other sensitive model properties in usernames.
- For Power Automate Blob transport, grant its Entra user or service principal only the required `Storage Blob Data Reader` scope, read one complete live dataset, and exclude `.staging`. Do not enable SFTP/hierarchical namespace or add a Storage firewall for this connector path.
- The deploying organization is responsible for Azure access controls, retention, monitoring and compliance with the AVG/GDPR.
