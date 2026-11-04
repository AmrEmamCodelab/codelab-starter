/*
 * Public API of @codelab/ng-security. Everything not exported here is internal and may change in
 * any release.
 */

export * from './core.ts';

export {
  RequestNotSignedError,
  SIGNED_REQUEST_NONCE,
  randomNonce,
  signRequest,
  type SignedRequest,
  type SigningContext,
} from './signing/request-signature.ts';
export {
  SECURITY_CLOCK,
  SECURITY_PROTOCOL,
  SESSION_VERIFICATION_KEY_SOURCE,
  SIGNED_SERVICE_URL_CLASSIFIER,
  SYSTEM_CLOCK,
  responseVerificationKeySource,
  type SessionVerificationKeySource,
} from './angular/tokens.ts';
export { SessionSigningKeyMissingError, signatureInterceptor } from './angular/signature.interceptor.ts';
export { responseSignatureInterceptor } from './angular/response-signature.interceptor.ts';
export { provideCodelabSecurity, type CodelabSecurityOptions } from './angular/providers.ts';
