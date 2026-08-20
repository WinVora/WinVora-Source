# WinVora code-signing strategy

WinVora is currently distributed with a SHA-256 checksum but without an
Authenticode signature. The checksum proves that a downloaded file matches the
published release asset; it does not establish the publisher identity by itself.

## Intended release process

1. Obtain an Authenticode certificate from a publicly trusted provider when the
   project budget permits it. Free self-signed certificates are not suitable for
   public SmartScreen trust.
2. Keep the private key outside the repository. For CI, use a protected GitHub
   Environment and an encrypted certificate secret or a managed signing service.
3. Sign the final installer after Inno Setup has produced it and before checksums
   are generated.
4. Use RFC 3161 timestamping so an existing signature remains valid after the
   certificate expires.
5. Verify the signature in CI with `Get-AuthenticodeSignature` and fail the
   release job unless its status is `Valid`.
6. Generate `SHA256SUMS.txt` from the signed installer and publish both files.

## Current user guidance

Until signing is available, users should download WinVora only from the official
`WinVora/WinVora-Releases` repository and may compare the installer against
`SHA256SUMS.txt`. The README and release notes must state clearly that SmartScreen
can display an unknown-publisher warning.

No certificate, password, token, or private-key material may be committed to this
repository.
