# Contributing

Thank you for contributing to Somtoday2MicrosoftSDS.

## Start with the documentation route

Read [the project core](docs/PROJECT_CORE.md) before every change, then use its task-based routing matrix to select only the relevant contracts and guides. The core and focused contracts define intended behavior; [the deviation register](docs/DEVIATIONS.md) records known implementation gaps.

Read the Dutch operator [README](README.md) and operations guides only for changes to configuration, deployment, releases, usage, or other externally visible behavior.

## Before opening a change

Open an issue first for substantial behavioral or public-configuration changes. Keep changes focused, preserve unrelated work, and explain operational and privacy impact. Do not silently change component boundaries, data flow, side effects, failure behavior, or confirmed intent.

Never submit credentials, access tokens, connection strings, personal data, production CSV files, authentication bodies, or unsafe exception detail. Do not update the Somtoday OpenAPI specification or generated client without explicit scope and a record of its source, specification version, and generation tool/version.

By submitting a contribution, you agree that it is licensed under `AGPL-3.0-or-later` and that you have the right to contribute it on those terms.

## Validate the change

Follow [the development guide](docs/DEVELOPMENT.md) for build, test, publish, Bicep, and container commands. Add or update tests for behavioral changes, preserve cancellation through network and storage operations, and report validations not run. Do not commit generated build output.

## Pull requests

- Use a descriptive title and link related issues.
- Note breaking configuration changes explicitly.
- Update the authoritative core, contract, architecture, or deviation entry when its subject changes.
- Update operator-facing documentation only when the change is externally visible.
- Update third-party notices when dependencies change.
