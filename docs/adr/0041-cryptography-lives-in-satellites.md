# 41. Cryptography lives in satellites

Date: 2026-09-26

## Status

Accepted on 2026-09-26, by the maintainer. It applies [9](0009-a-dependency-free-core-plus-satellites.md)
to signatures and certificate encryption, and details [18](0018-signing-space-reserved.md). M16, M26 and
M27 implement it.

## Context

Three features need the Cryptographic Message Syntax:

- **signing** (M26) — a CMS `SignedData` over the byte range, and RFC 3161 time-stamp tokens;
- **signature validation** (M27) — parsing and checking the same structures in received documents;
- **the public-key security handler** (ISO 32000-2, 7.6.5) — a file encrypted for one or more certificates
  carries its key in a CMS `EnvelopedData`.

In .NET these types — `SignedCms`, `EnvelopedCms`, `Rfc3161TimestampRequest` — live in
`System.Security.Cryptography.Pkcs`, a package outside the shared framework. Invariant 1 bars it from the
core. Invariant 12 still requires the core to read every valid PDF, and a file encrypted for a certificate
is valid.

What the shared framework does provide is enough for the rest: AES in CBC and GCM modes, SHA-2, HMAC, and
`System.Formats.Asn1`. Time stamps, revocation and trust lists also imply network access, which invariant 6
forbids unless the caller supplies it.

## Decision

We will keep CMS out of the core, and give the core everything the shared framework allows.

- **The core** implements the standard (password) security handler — RC4 and AES-128 and 256 —, reads
  AES-GCM encryption and the integrity MAC of ISO/TS 32003 and 32004 with the framework's `AesGcm` and HMAC,
  and computes everything about signatures that needs no cryptography (M4: revisions, byte-range coverage,
  change classification).
- **The core detects the public-key security handler** and reports it under a stable diagnostic code, so a
  caller knows why the document's content is not readable without the satellite.
- **`AdCodicem.Pdf.Signing`** takes `System.Security.Cryptography.Pkcs` and holds every CMS use: signing
  (M26), validation (M27), and decrypting and encrypting for certificates (M26).
- **Network access** — a time-stamp authority, OCSP, CRLs, trusted lists — is off by default. A caller
  enables it by supplying a client; the signing time, the validation time and the trust anchors are inputs,
  never read from the clock or a system store unless the caller says so.

## Consequences

- A document encrypted for a certificate is detected by the core and opened with the signing satellite;
  "Signing" becomes the name of the package that holds all certificate cryptography.
- No security-sensitive ASN.1 parsing of hostile input is written here: the parsing is the framework's.
- `docs/architecture.md`'s package table says that the signing satellite depends on
  `System.Security.Cryptography.Pkcs`.
- **Rejected** — a hand-written minimal CMS on `System.Formats.Asn1` in the core (security-sensitive parsing
  of hostile input to maintain ourselves); relaxing invariant 1 for Microsoft's out-of-band packages (breaks
  a non-negotiable invariant and invites more exceptions).
- **What would reopen it** — `System.Security.Cryptography.Pkcs` moving into the shared framework, which
  would let the core decrypt certificate-encrypted files by itself.
