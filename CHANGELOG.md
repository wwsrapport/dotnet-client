# Changelog

All notable changes to this project are documented in this file.

## [0.3.0] - 2026-08-18

### Added

- OAuth 2.0 client credentials with concurrency-safe short-lived token caching.
- Municipality, purpose, case and client context headers.
- Batch, human-review, tenant-export and controlled offboarding resources.
- API version, request ID and correlation ID headers.

### Changed

- The existing API-key constructor remains backwards compatible.
- NLGov `400 invalid_input` maps to `WwsrapportValidationException` alongside HTTP 422.

## [0.2.1] - 2026-08-06

### Added

- Added a manually triggered Trusted Publishing workflow using GitHub OIDC.\n- Added automated release metadata and validation for the 0.2.1 release.

## [0.2.0] - 2026-08-06

### Added

- Solana registry verification for reports.
- BAG HMAC reference derivation through the Partner API.
- Registry search by BAG ID through the Partner API.
- Complete catalog of supported WWSrapport webhook event types.

### Changed

- Documentation and examples now describe the attestation-based Solana flow.
- Client identification has been updated to version 0.2.0 where applicable.

## [0.1.0] - 2026-07-25

- Initial public client release.
