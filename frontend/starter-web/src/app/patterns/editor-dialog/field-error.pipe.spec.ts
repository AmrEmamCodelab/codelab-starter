import { TestBed } from '@angular/core/testing';
import { parseApiError } from '@codelab/ng-security';
import { beforeEach, describe, expect, it } from 'vitest';
import { patternTestProviders } from '../patterns.testing';
import { FieldErrorPipe } from './field-error.pipe';

const BAD_REQUEST = 400;

describe('FieldErrorPipe', () => {
  let pipe: FieldErrorPipe;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [...patternTestProviders(), FieldErrorPipe] });
    pipe = TestBed.inject(FieldErrorPipe);
  });

  it('translates the first error of the field from an ApiError', () => {
    const error = parseApiError({
      status: BAD_REQUEST,
      error: { code: 'VALIDATION_FAILED', errors: { email: ['EMAIL_INVALID', 'EMAIL_TAKEN'] } },
    });
    expect(pipe.transform(error, 'email')).toBe('errors.EMAIL_INVALID');
    expect(pipe.transform(error, 'userName')).toBeNull();
  });

  it('reads a field-error map', () => {
    expect(pipe.transform({ userName: ['errors.USER_NAME_REQUIRED'] }, 'userName')).toBe('errors.USER_NAME_REQUIRED');
  });

  it('is null without errors', () => {
    expect(pipe.transform(null, 'email')).toBeNull();
    expect(pipe.transform(undefined, 'email')).toBeNull();
  });
});
