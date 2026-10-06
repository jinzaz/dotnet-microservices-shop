---
name: certificate-service
description: Change the certificate store (CertificateDb), certificate purposes/rotation rules, import/export/generate APIs, ACME issuance and renewal, TLS deployment for nginx, or how Identity/BFFs load signing, encryption and body-encryption keys.
---

# Certificate store

`MSA.CertificateService` is the only writer of `CertificateDb` (schema `pki`). Identity and both BFFs read their own rows directly through `MSA.Certificates` (`AddMsaCertificates`, `UseMsaCertificates`) — never through an HTTP API, because Identity needs its signing key before it can issue the tokens such an API would require. Keep the columns used by the raw SQL in `MSA.Certificates` (`CertificateStoreLoader`, `CertificateDbXmlRepository`) in sync with the EF mapping.

Every consumer has its own KEK (`MSA_CERT_KEK_<CONSUMER>`, 32 bytes base64). Private keys are PKCS#8 encrypted with AES-GCM under the owning consumer's KEK, with the row id, consumer and purpose as associated data. All services share one database account, so isolation comes from the KEKs: only the certificate service holds all of them. Never store or log a decrypted key.

Stages are derived from time, never stored: the active certificate of a (consumer, purpose, slot) group is the latest one whose `activate_at` has passed; the previous one stays usable for the purpose's retention, then retires. Revoking the active certificate rolls back to the previous one, and is refused when nothing would remain. Every write must bump `updated_at` — consumers poll `count + max(updated_at)` to notice changes.

Rules per purpose live in `CertificatePurposes` (accepted keys, activation lead, retention, rotation period). OpenIddict signing keys are published in JWKS from staging but only the store's active certificate signs (`ServerActiveCredentialSelector`); do not rely on OpenIddict's own NotAfter-based choice. Options rebuild on the store's change token; local validation options rebuild after server options, never on the same token.

`CertificateRenewalService` runs hourly (and on demand): auto-rotate internal keys, renew due ACME subscriptions (2/3 of lifetime, exponential backoff, a row lease per subscription), deploy active TLS certificates to `Tls:DeployDirectory`, and warn about expiring certificates nobody renews. Wildcard domains require DNS-01. DNS providers implement `IDnsChallengeProvider` and read credentials only from environment variables.

Private-key export needs the write scope here and the separate `system.certificate-keys.query` permission in BM; it is audited and blocked for non-exportable certificates.
