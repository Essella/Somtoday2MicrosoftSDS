# Release guide

## Supported artifacts

The supported release artifacts are:

```text
ghcr.io/essella/somtoday2microsoftsds:VERSION
ghcr.io/essella/somtoday2microsoftsds:VERSION-refresh.RUN_NUMBER
CSV bootstrap zip assets attached to the GitHub Release
```

The current container workflow targets `linux/amd64`, requests an SBOM attestation, and creates a build-provenance attestation for release and refresh images. A Windows application archive is not supported.

The workflow refreshes the latest published, non-prerelease GitHub Release every Thursday at 05:11 UTC. It can also run a refresh through `workflow_dispatch`. A refresh checks out the release tag, explicitly pulls the latest `mcr.microsoft.com/dotnet/sdk:10.0` and `mcr.microsoft.com/dotnet/runtime:10.0` base images, and rebuilds the same source without changing the application version. This weekly schedule picks up the regular monthly .NET rebuilds and limits delay for base-image and out-of-band security updates. See the Microsoft [.NET image update policy](https://github.com/dotnet/dotnet-docker/blob/main/README.aspnet.md#image-update-policy) and [container vulnerability workflow](https://github.com/dotnet/dotnet-docker/blob/main/documentation/vulnerability-reporting.md#f-rebuild-your-image-to-get-the-latest-base).

Release publication is gated by the same infrastructure validation as CI. Before validation, both workflows install Bicep when required, update it to the latest version, and log the checked-out commit SHA and Bicep version. The validation regenerates both ARM templates in a temporary directory, compiles both example parameter files, and compares the canonical generated output with the committed files in `infra/`. It reports each stale file with its Bicep source and does not modify the checkout. The remaining validation checks the tag-based Environment hand-off and fixed Job settings. A new Bicep version can change generated ARM output. In that case, regenerate and commit the ARM template before release.

CSV bootstrap zip assets are generated during the release workflow (not stored as tracked repository files). They exist to support one-time SDS connector bootstrap: administrators can upload a header-only CSV set that matches the chosen SDS connector variant, including environments where local script execution is restricted.

The project owner generated the tracked client code with Visual Studio and confirms that no additional redistribution approval is required. The repository is new and has never contained a committed secret; a full-history secret scan is not a release prerequisite.

## Versioning and tags

Create a GitHub Release with a four-part tag such as `v1.2.3.4`. Every component must be between 0 and 65534. The release version is used for application metadata and the container tag. The workflow also publishes `sha-COMMIT` and `latest` tags.

A scheduled or manual refresh keeps that four-part application version and publishes `VERSION-refresh.RUN_NUMBER`. `RUN_NUMBER` is the monotonically increasing number for this GitHub Actions workflow. It does not reset for each release and is not parsed as a .NET `System.Version` component, so the 65534 component limit does not apply to it. The workflow treats each refresh tag as immutable and rejects a refresh if its tag already exists. Start a new manual workflow run instead of rerunning a published refresh.

Release publication writes `VERSION`, `sha-COMMIT`, and `latest`. Refresh publication writes only `VERSION-refresh.RUN_NUMBER` and `latest`; it never moves the original version or commit tag. The moving `latest` tag therefore identifies the newest release or successful refresh, while a version tag identifies the original release image and a refresh tag or digest identifies one maintenance rebuild.

Create a release tag only from the exact commit for which CI has completed successfully. If release infrastructure validation fails, correct the source or generated ARM templates, pass CI again, and create a new tag. Do not republish the existing failed tag.

Each GitHub Release also publishes four CSV bootstrap zip assets:

- `v1-no-guardians.zip`
- `v1-with-guardians.zip`
- `v2-no-guardians.zip`
- `v2-with-guardians.zip`

Local builds default to `0.0.0.0`. The supported deployment template uses `latest` so Container Apps Jobs receive successful refreshes automatically. Use the unique refresh tag or digest for audit and rollback. An operator who replaces `latest` with a fixed reference must update that reference explicitly to receive future refreshes.

**Code-observed facts:** Workflow actions currently use major-version tags rather than full commit SHAs. Dependabot monitors NuGet, Docker, and GitHub Actions dependencies weekly. No policy requiring SHA pinning has been confirmed.

## First public release administration

After the first release:

1. Set the GHCR package to **Public** in [GitHub Packages](https://docs.github.com/en/packages/learn-github-packages/introduction-to-github-packages).
2. Rename the repository to `Somtoday2MicrosoftSDS` if still required.
3. Configure branch rulesets, required CI, CodeQL, secret scanning, and push protection.

Pull the public image with:

```powershell
docker pull ghcr.io/essella/somtoday2microsoftsds:latest
```

The image runs as non-root user `1654`. Azure needs no registry credentials after the package is public.

## Dependency inventory authority

The following is confirmed project policy:

- Project files and the Dockerfile are authoritative for intended build inputs.
- The release or refresh SBOM is the primary generated inventory of components detected in one specific image digest.
- `THIRD-PARTY-NOTICES.md` and upstream license material are authoritative for licensing information and required attribution.
- A conflict between these sources must be investigated. The SBOM does not automatically override declared build inputs, notices, or upstream license material.

## Licensing

The project is released under AGPL-3.0-or-later. See [LICENSE](../../LICENSE), [NOTICE.md](../../NOTICE.md), and [THIRD-PARTY-NOTICES.md](../../THIRD-PARTY-NOTICES.md).
