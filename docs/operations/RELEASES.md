# Release guide

## Supported artifact

The supported release artifact is:

```text
ghcr.io/essella/somtoday2microsoftsds:VERSION
```

The current container workflow targets `linux/amd64`, requests an SBOM attestation, and creates a build-provenance attestation. A Windows application archive is not supported.

The project owner generated the tracked client code with Visual Studio and confirms that no additional redistribution approval is required. The repository is new and has never contained a committed secret; a full-history secret scan is not a release prerequisite.

Current workflow mismatches are `DEV-001` and `DEV-002` in [the deviation register](../DEVIATIONS.md).

## Versioning and tags

Create a GitHub Release with a four-part tag such as `v1.2.3.4`. Every component must be between 0 and 65534. The release version is used for application metadata and the container tag. The workflow also publishes `sha-COMMIT` and `latest` tags.

Local builds default to `0.0.0.0`. Pin production to a release tag or digest rather than `latest`.

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
- The release SBOM is the primary generated inventory of components detected in one specific image digest.
- `THIRD-PARTY-NOTICES.md` and upstream license material are authoritative for licensing information and required attribution.
- A conflict between these sources must be investigated. The SBOM does not automatically override declared build inputs, notices, or upstream license material.

## Licensing

The project is released under AGPL-3.0-or-later. See [LICENSE](../../LICENSE), [NOTICE.md](../../NOTICE.md), and [THIRD-PARTY-NOTICES.md](../../THIRD-PARTY-NOTICES.md).
