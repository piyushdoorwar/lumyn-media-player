# Ubuntu PPA Setup Guide

This guide explains how to publish Lumyn to an Ubuntu Personal Package Archive (PPA).

## Prerequisites

- Ubuntu Launchpad account
- GPG key registered on Launchpad
- PPA created at `ppa:piyushdoorwar/lumyn`
- GitHub Actions secrets listed below

## GitHub Secrets Required

Set these in **GitHub repo → Settings → Secrets and variables → Actions**.

### `GPG_PRIVATE_KEY`

Your GPG private key in ASCII-armored format.

Export it locally:

```bash
gpg --armor --export-secret-keys <YOUR_GPG_KEY_FINGERPRINT> > private_key.asc
```

Secret value: paste the full contents of `private_key.asc`, including:

```text
-----BEGIN PGP PRIVATE KEY BLOCK-----
...
-----END PGP PRIVATE KEY BLOCK-----
```

If you use the same Launchpad-registered signing key as Transmux, use the same secret value there.

### `GPG_PASSPHRASE`

Your GPG private key passphrase.

Secret value: the exact passphrase for the private key exported in `GPG_PRIVATE_KEY`.

If the key has no passphrase, this secret may be left empty or omitted. The workflow supports both protected and unprotected keys.

## How It Works

When you push a tag such as:

```bash
git tag v1.2.3
git push origin v1.2.3
```

The release workflow:

1. Extracts the version from the tag.
2. Vendors NuGet packages into `./packages`.
3. Stages `packaging/debian/` into the root-level `debian/` directory expected by Debian tooling.
4. Updates `debian/changelog`.
5. Builds a Debian source package.
6. Signs the source package with your GPG key.
7. Uploads to `ppa:piyushdoorwar/lumyn` using `dput`.
8. Launchpad builds the binary package.

## Install Command For Users

Once published:

```bash
sudo add-apt-repository ppa:piyushdoorwar/lumyn
sudo apt update
sudo apt install lumyn
```

## Troubleshooting

- If upload authentication fails, confirm the GPG public key is registered on Launchpad and the PPA exists.
- If signing fails, confirm `GPG_PRIVATE_KEY` and `GPG_PASSPHRASE` match.
- If Launchpad rejects the upload, make sure the same version has not already been uploaded. PPAs do not allow re-uploading the same version.
