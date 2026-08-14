# OfficeIMO binary provenance

- Source: https://github.com/EvotecIT/OfficeIMO
- Commit: `9e1e019ea8dbbb5595cf376dc76a71b09b921f54`
- Upstream version: `3.2.2`
- Built for: `net8.0`, Release
- Build date: 2026-08-14
- License: MIT (`LICENSE.txt`)

The pinned build is used for append-only PDF signature revisions so an additional
signature can be added without rewriting bytes covered by earlier signatures.

CryptoSigTool carries a narrow source patch (`CryptoSigTool.patch`) that permits
another approval-signature revision when the document has no DocMDP prohibition,
encryption blocker, usage-rights blocker, or signature-field lock. The append-only
writer and byte-prefix preservation checks remain unchanged.
