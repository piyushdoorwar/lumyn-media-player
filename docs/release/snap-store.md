# Snap Store Setup Guide

This guide explains how the release workflow publishes Lumyn's `.snap` package to the Snap Store.

## Prerequisites

- Snap name registered in the Snap Store: `lumyn`
- GitHub Actions secret listed below
- Snapcraft installed locally for generating store credentials

## GitHub Secret Required

Set this in **GitHub repo -> Settings -> Secrets and variables -> Actions**.

### `SNAPCRAFT_STORE_CREDENTIALS`

Value: the full contents of an exported Snapcraft login file for the `lumyn` snap.

Generate it locally:

```bash
snapcraft login
snapcraft export-login --snaps=lumyn --channels=stable,edge snapcraft-login
```

Then open `snapcraft-login` and paste the entire file contents into the `SNAPCRAFT_STORE_CREDENTIALS` secret.

## Release Behavior

The `linux-snap` job in `.github/workflows/release.yml`:

1. Builds the snap with `canonical/action-build@v1`.
2. Uploads the `.snap` file as a GitHub Actions artifact.
3. Publishes the same `.snap` to the Snap Store with `snapcraft upload --release`.

Channel selection:

- Versions without a prerelease suffix, such as `1.2.3`, publish to `stable`.
- Versions with a prerelease suffix, such as `1.2.3-beta.1` or `0.0.0-dev`, publish to `edge`.

## Notes

- If the store upload fails with an authentication error, regenerate `SNAPCRAFT_STORE_CREDENTIALS` and confirm it is scoped to the `lumyn` snap.
- If the store review rejects the snap, check whether the requested interfaces need store review or manual connection.
- If the same snap revision has already been uploaded, rebuild with a new version before retrying.
