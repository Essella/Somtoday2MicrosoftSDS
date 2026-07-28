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
- The deploying organization is responsible for Azure access controls, retention, monitoring and compliance with the AVG/GDPR.
