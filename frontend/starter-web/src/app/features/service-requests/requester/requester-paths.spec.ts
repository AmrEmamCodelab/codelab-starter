import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import {
  SERVICE_REQUESTS_REQUESTER_BASE_LINK,
  SERVICE_REQUESTS_REQUESTER_DEFAULT_BASE_LINK,
  serviceRequestIdOf,
  serviceRequestsRequesterLinks,
} from './requester-paths';
import { TEST_SERVICE_REQUEST_ID } from './requester.testing';

describe('serviceRequestsRequesterLinks', () => {
  const links = serviceRequestsRequesterLinks('/service-requests');

  it('builds every page link under the base', () => {
    expect(links.dashboard).toBe('/service-requests');
    expect(links.mine).toBe('/service-requests/mine');
    expect(links.create).toBe('/service-requests/new');
    expect(links.draft('abc')).toBe('/service-requests/drafts/abc');
    expect(links.detail('abc')).toBe('/service-requests/abc');
  });

  it('encodes an id as one path segment', () => {
    expect(links.detail('a/b?c')).toBe('/service-requests/a%2Fb%3Fc');
    expect(links.draft('a/b')).toBe('/service-requests/drafts/a%2Fb');
  });

  it('follows a base under a prefix', () => {
    expect(serviceRequestsRequesterLinks('/my/service-requests').mine).toBe('/my/service-requests/mine');
  });

  it('defaults the base link token to the app root mount', () => {
    expect(TestBed.inject(SERVICE_REQUESTS_REQUESTER_BASE_LINK)).toBe(SERVICE_REQUESTS_REQUESTER_DEFAULT_BASE_LINK);
    expect(SERVICE_REQUESTS_REQUESTER_DEFAULT_BASE_LINK).toBe('/service-requests');
  });
});

describe('serviceRequestIdOf', () => {
  it('accepts a token-shaped id and refuses anything else', () => {
    expect(serviceRequestIdOf(TEST_SERVICE_REQUEST_ID)).toBe(TEST_SERVICE_REQUEST_ID);
    expect(serviceRequestIdOf('42')).toBeNull();
    expect(serviceRequestIdOf(undefined)).toBeNull();
    expect(serviceRequestIdOf(null)).toBeNull();
  });
});
