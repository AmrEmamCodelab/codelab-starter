import { describe, expect, it } from 'vitest';
import {
  ServiceRequestValidationScopes,
  validateServiceRequest,
  type ServiceRequestValidationInput,
  type ServiceRequestValidationScope,
} from './service-request-rules';
import fixtureJson from './validation-parity.fixtures.json';

/*
 * The FE half of the FE/BE validation parity contract. The backend parity test (Modules/ServiceRequests tests) runs
 * the SAME fixture file through the FluentValidation validators; both must give exactly `expected`.
 */

interface ParityCase {
  readonly name: string;
  readonly scope: ServiceRequestValidationScope;
  readonly input: ServiceRequestValidationInput;
  readonly expected: { readonly valid: boolean; readonly errorFields: readonly string[] };
}

interface ParityFixture {
  readonly today: string;
  readonly cases: readonly ParityCase[];
}

const fixture = fixtureJson as ParityFixture;
const MINIMUM_CASES = 20;
const SCOPES: readonly string[] = Object.values(ServiceRequestValidationScopes);

describe('validation parity fixture', () => {
  it('is well formed: a fixed today, enough cases, known scopes, unique names, sorted error fields', () => {
    expect(fixture.today).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(fixture.cases.length).toBeGreaterThanOrEqual(MINIMUM_CASES);
    expect(new Set(fixture.cases.map((parityCase) => parityCase.name)).size).toBe(fixture.cases.length);
    for (const parityCase of fixture.cases) {
      expect(SCOPES).toContain(parityCase.scope);
      expect(parityCase.expected.valid).toBe(parityCase.expected.errorFields.length === 0);
      expect([...parityCase.expected.errorFields].sort()).toEqual(parityCase.expected.errorFields);
    }
  });

  it('covers every scope with both accepted and refused inputs', () => {
    for (const scope of SCOPES) {
      const ofScope = fixture.cases.filter((parityCase) => parityCase.scope === scope);
      expect(ofScope.some((parityCase) => parityCase.expected.valid)).toBe(true);
      expect(ofScope.some((parityCase) => !parityCase.expected.valid)).toBe(true);
    }
  });

  it.each(fixture.cases.map((parityCase) => [parityCase.name, parityCase] as const))('%s', (_name, parityCase) => {
    const result = validateServiceRequest(parityCase.input, parityCase.scope, fixture.today);

    expect({ valid: result.valid, errorFields: result.errorFields }).toEqual(parityCase.expected);
  });
});
