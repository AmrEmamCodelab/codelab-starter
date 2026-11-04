/*
 * The framework-free half of @codelab/ng-security (importable from Node, e.g. by the golden-vector
 * runner): protocol names, canonical framing, signature grammar and digests, response verification,
 * the session key store, wire ids and the problem+json parser. No Angular import below this line.
 */

export {
  SCHEME_REVISION,
  TEST_PROTOCOL_PREFIX,
  InvalidProtocolPrefixError,
  createSecurityProtocol,
  isValidProtocolPrefix,
  type SecurityProtocol,
} from './protocol/security-protocol.ts';

export {
  ALLOWED_METHODS,
  buildCanonical,
  buildCanonicalBytes,
  canonicalMethod,
  normalise,
  normalizePathForSigning,
  queryOf,
  type AllowedMethod,
  type CryptoBytes,
} from './canonical/canonical.ts';

export { BASE64URL_ALPHABET, base64UrlEncode, base64UrlLength, decodeBase64UrlStrict, isBase64Url } from './signing/base64url.ts';
export {
  UNSIGNED_BODY,
  buildRequestSigningInput,
  buildRequestSigningInputBytes,
  buildResponseSigningInput,
  buildResponseSigningInputBytes,
  computeMac,
  hashBody,
  verifyMac,
  type SignedRequestFields,
  type SignedResponseFields,
} from './signing/signature-canonical.ts';
export {
  MAC_BYTES,
  MAC_LENGTH,
  MAX_SIGNATURE_HEADER_LENGTH,
  MAX_TIMESTAMP_DIGITS,
  MAX_TIMESTAMP_SECONDS,
  MIN_SIGNATURE_HEADER_LENGTH,
  NONCE_BYTES,
  NONCE_LENGTH,
  RESPONSE_SIGNATURE_HEADER_LENGTH,
  SIGNATURE_SCHEME_VERSION,
  formatResponseSignatureHeader,
  formatSignatureHeader,
  isTimestamp,
  parseResponseSignatureHeader,
  parseSignatureHeader,
  timestampDigits,
  type SignatureHeaderInput,
  type SignatureHeaderParts,
} from './signing/signature-header.ts';
export { SignatureProblemCodes, isSignatureProblemCode, type SignatureProblemCode } from './signing/signature-problem-codes.ts';
export { createApiRootClassifier, type ApiRootClassifierOptions, type ServiceUrlClassifier } from './signing/service-url.ts';

export {
  ResponseSignatureError,
  ResponseSignatureFailures,
  isResponseSignatureError,
  mayOmitResponseSignature,
  verifyResponseSignature,
  type ReceivedResponse,
  type ResponseSignatureFailure,
  type VerificationContext,
} from './verification/response-signature.ts';
export { decodeForDelivery, type DeliveredBody, type ResponseType } from './verification/response-body.ts';

export {
  SessionKeyStore,
  isStoredKeyRecord,
  type EpochClock,
  type SessionKeyStoreDependencies,
  type SessionSigningKeys,
} from './session/session-keys.store.ts';
export {
  MIN_SESSION_KEY_BYTES,
  SUPPORTED_SIGNING_ALGORITHM,
  SigningEnvironmentUnsupportedError,
  SigningMaterialRejectedError,
  SigningRefusalReasons,
  type SigningSessionBlock,
} from './session/signing-material.ts';
export {
  DEFAULT_SESSION_PERSISTENCE_NAMES,
  indexedDbKeyRecordStore,
  localStorageMarker,
  type KeyRecordStore,
  type SessionPersistenceNames,
  type SigningSessionMarker,
} from './session/session-persistence.ts';

export {
  NotAWireIdError,
  WIRE_ID_PAYLOAD_LENGTH,
  WIRE_ID_VERSION,
  asId,
  defineIdDomains,
  isWireId,
  toIdSet,
  tryAsId,
  type WireId,
} from './wire-id/wire-id.ts';

export {
  ApiErrorCodes,
  ERROR_MESSAGE_KEY_PREFIX,
  apiErrorMessageKey,
  parseApiError,
  parseFieldErrors,
  type ApiError,
  type ApiFieldError,
  type HttpErrorLike,
} from './errors/api-error.ts';
export { HttpStatus, codeForStatus, isUnlabelledOutage, type ApiErrorCode } from './errors/api-error-codes.ts';
export { isClientTraceId, referenceCodeFrom } from './errors/reference-code.ts';
