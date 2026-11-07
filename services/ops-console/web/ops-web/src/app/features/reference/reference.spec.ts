import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, type ParamMap } from '@angular/router';
import { DGA_CLIPBOARD } from '@codelab/ng-dga';
import { BehaviorSubject } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { OpsApiClient } from '../../core/api/ops-api.client';
import type { ReferenceTrace } from '../../core/api/ops-api.models';
import { FakeOpsApiClient, opsTestProviders, textOf } from '../../core/testing/ops.testing';
import { ReferenceKeys, levelTone, normalizeReferenceCode, webUrlOrNull } from './reference-code';
import { CopyOutcome, ReferenceLookupStore } from './reference-lookup.store';
import { ReferenceLookupPage } from './reference-lookup.page';

const TRACE: ReferenceTrace = {
  referenceCode: '7D2C1-E9A04',
  correlationId: '7d2c1e9a04b65f38c1a9e2d70b4f6a15',
  traceId: '7d2c1e9a04b65f38c1a9e2d70b4f6a15',
  firstSeen: '2026-11-07T08:00:00Z',
  request: { method: 'POST', path: '/api/records', status: 500 },
  events: [
    { timestamp: '2026-11-07T08:00:02Z', level: 'Error', message: 'second' },
    { timestamp: '2026-11-07T08:00:01Z', level: 'Information', message: 'first' },
  ],
  seqUrl: 'https://seq.internal.example/#/events?filter=x',
  seqAvailable: true,
};

describe('normalizeReferenceCode', () => {
  it.each([
    ['7D2C1-E9A04', '7D2C1-E9A04'],
    ['7d2c1-e9a04', '7D2C1-E9A04'],
    [' 7d2c1e9a04 ', '7D2C1-E9A04'],
    ['7D2C1 - E9A04', '7D2C1-E9A04'],
    ['7d2c1e9a04b65f38c1a9e2d70b4f6a15', '7D2C1-E9A04'],
    ['7d2c1e9a-04b6-5f38-c1a9-e2d70b4f6a15', '7D2C1-E9A04'],
  ])('reads %s as %s', (input, expected) => {
    expect(normalizeReferenceCode(input)).toBe(expected);
  });

  it.each(['', 'ABC', '4BF92-F357G', '7D2C1-E9A040', '<script>'])('refuses %s', (input) => {
    expect(normalizeReferenceCode(input)).toBeNull();
  });
});

describe('reference helpers', () => {
  it('maps Seq levels to tones', () => {
    expect(levelTone('Error')).toBe('error');
    expect(levelTone('Fatal')).toBe('error');
    expect(levelTone('Warning')).toBe('warning');
    expect(levelTone('Information')).toBe('info');
    expect(levelTone('Debug')).toBe('neutral');
    expect(levelTone('Odd')).toBe('neutral');
  });

  it('offers only http(s) links', () => {
    expect(webUrlOrNull('https://seq.example/x')).toBe('https://seq.example/x');
    expect(webUrlOrNull('http://seq.example/x')).toBe('http://seq.example/x');
    expect(webUrlOrNull('javascript:alert(1)')).toBeNull();
    expect(webUrlOrNull('not a url')).toBeNull();
    expect(webUrlOrNull(null)).toBeNull();
  });
});

describe('ReferenceLookupStore', () => {
  let client: FakeOpsApiClient;
  let clipboard: { write: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    client = new FakeOpsApiClient();
    clipboard = { write: vi.fn(async () => true) };
    TestBed.configureTestingModule({
      providers: [...opsTestProviders(), ReferenceLookupStore, { provide: OpsApiClient, useValue: client }, { provide: DGA_CLIPBOARD, useValue: clipboard }],
    });
  });

  it('submit validates and flags bad input without calling the BFF', () => {
    const store = TestBed.inject(ReferenceLookupStore);
    expect(store.submit('nope')).toBeNull();
    expect(store.invalid()).toBe(true);
    expect(store.submit('7d2c1e9a04')).toBe('7D2C1-E9A04');
    expect(store.invalid()).toBe(false);
    expect(client.calls).toEqual([]);
  });

  it('looks a code up once and sorts its events by time', () => {
    const store = TestBed.inject(ReferenceLookupStore);
    store.lookup('7d2c1-e9a04');
    store.lookup('7D2C1-E9A04');
    expect(client.calls).toEqual(['reference:7D2C1-E9A04']);
    client.answer('reference:7D2C1-E9A04', TRACE);
    expect(store.events().map((event) => event.message)).toEqual(['first', 'second']);
    expect(store.seqUrl()).toBe(TRACE.seqUrl);
    store.lookup('7D2C1-E9A04');
    expect(client.calls).toHaveLength(1);
    store.reload();
    expect(client.calls).toHaveLength(2);
  });

  it('a 404 is "not found", not an error', () => {
    const store = TestBed.inject(ReferenceLookupStore);
    store.lookup('AAAAA-BBBBB');
    client.fail('reference:AAAAA-BBBBB', 404, { code: 'NOT_FOUND' });
    expect(store.notFound()).toBe(true);
    expect(store.error()).toBeNull();
  });

  it('another failure is an error with its reference code', () => {
    const store = TestBed.inject(ReferenceLookupStore);
    store.lookup('AAAAA-BBBBB');
    client.fail('reference:AAAAA-BBBBB', 503, { code: 'GATEWAY_UPSTREAM_UNAVAILABLE', correlationId: 'cccccccccccccccccccccccccccccccc' });
    expect(store.notFound()).toBe(false);
    expect(store.error()?.referenceCode).toBe('CCCCC-CCCCC');
  });

  it('an empty or malformed URL value clears the screen', () => {
    const store = TestBed.inject(ReferenceLookupStore);
    store.lookup('AAAAA-BBBBB');
    client.answer('reference:AAAAA-BBBBB', TRACE);
    store.lookup(null);
    expect(store.trace()).toBeNull();
    expect(store.code()).toBeNull();
    expect(store.invalid()).toBe(false);
    store.lookup('garbage');
    expect(store.invalid()).toBe(true);
  });

  it('copies the Seq link and reports the outcome', async () => {
    const store = TestBed.inject(ReferenceLookupStore);
    await store.copySeqLink();
    expect(clipboard.write).not.toHaveBeenCalled();
    store.lookup('AAAAA-BBBBB');
    client.answer('reference:AAAAA-BBBBB', TRACE);
    await store.copySeqLink();
    expect(clipboard.write).toHaveBeenCalledWith(TRACE.seqUrl);
    expect(store.copyOutcome()).toBe(CopyOutcome.Copied);
    clipboard.write.mockResolvedValueOnce(false);
    await store.copySeqLink();
    expect(store.copyOutcome()).toBe(CopyOutcome.Failed);
  });

  it('does not offer a Seq link that is not http(s)', () => {
    const store = TestBed.inject(ReferenceLookupStore);
    store.lookup('AAAAA-BBBBB');
    client.answer('reference:AAAAA-BBBBB', { ...TRACE, seqUrl: 'javascript:alert(1)' });
    expect(store.seqUrl()).toBeNull();
  });
});

describe('ReferenceLookupPage', () => {
  let client: FakeOpsApiClient;
  let params: BehaviorSubject<ParamMap>;

  beforeEach(() => {
    client = new FakeOpsApiClient();
    params = new BehaviorSubject(convertToParamMap({}));
    TestBed.configureTestingModule({
      providers: [
        ...opsTestProviders(),
        { provide: OpsApiClient, useValue: client },
        { provide: ActivatedRoute, useValue: { queryParamMap: params } },
        { provide: DGA_CLIPBOARD, useValue: { write: async () => true } },
      ],
    });
  });

  function render() {
    const fixture = TestBed.createComponent(ReferenceLookupPage);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  function typeAndSubmit(root: HTMLElement, text: string): void {
    const input = root.querySelector<HTMLInputElement>('[data-testid="reference-code-input"] input');
    if (!input) throw new Error('no input');
    input.value = text;
    input.dispatchEvent(new Event('input'));
    root.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
  }

  it('looks up the code in the URL and renders the request, events and Seq actions', () => {
    params.next(convertToParamMap({ code: '7d2c1e9a04' }));
    const { fixture, root } = render();
    expect(client.calls).toEqual(['reference:7D2C1-E9A04']);
    client.answer('reference:7D2C1-E9A04', TRACE);
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="reference-trace"]')?.textContent).toContain('/api/records');
    expect(root.querySelector('[data-testid="seq-link"]')?.getAttribute('href')).toBe(TRACE.seqUrl);
    expect(root.querySelector('[data-testid="seq-link"]')?.getAttribute('rel')).toBe('noopener noreferrer');
    expect(root.querySelector<HTMLInputElement>('[data-testid="reference-code-input"] input')?.value).toBe('7D2C1-E9A04');
  });

  it('a submitted code goes into the URL', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const { root } = render();
    typeAndSubmit(root, 'aaaaa bbbbb');
    expect(client.calls).toEqual(['reference:AAAAA-BBBBB']);
    expect(navigate).toHaveBeenCalledWith([], expect.objectContaining({ queryParams: { code: 'AAAAA-BBBBB' } }));
  });

  it('a malformed code shows the field error and calls nothing', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const { fixture, root } = render();
    typeAndSubmit(root, 'oops');
    fixture.detectChanges();
    expect(root.textContent).toContain(ReferenceKeys.Invalid);
    expect(client.calls).toEqual([]);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('shows "not found" for a 404', () => {
    params.next(convertToParamMap({ code: 'AAAAA-BBBBB' }));
    const { fixture, root } = render();
    client.fail('reference:AAAAA-BBBBB', 404);
    fixture.detectChanges();
    expect(textOf(root, '[data-testid="reference-not-found"]')).toBe(`${ReferenceKeys.NotFound}|code=AAAAA-BBBBB`);
  });
});

describe('ReferenceLookupPage with a partial trace', () => {
  it('renders without request, ids or Seq link and warns that Seq did not answer', () => {
    const client = new FakeOpsApiClient();
    TestBed.configureTestingModule({
      providers: [
        ...opsTestProviders(),
        { provide: OpsApiClient, useValue: client },
        { provide: ActivatedRoute, useValue: { queryParamMap: new BehaviorSubject(convertToParamMap({ code: 'AAAAA-BBBBB' })) } },
        { provide: DGA_CLIPBOARD, useValue: { write: async () => true } },
      ],
    });
    const fixture = TestBed.createComponent(ReferenceLookupPage);
    fixture.detectChanges();
    client.answer('reference:AAAAA-BBBBB', {
      ...TRACE,
      correlationId: null,
      traceId: null,
      firstSeen: null,
      request: null,
      events: [],
      seqUrl: null,
      seqAvailable: false,
    });
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[data-testid="reference-trace"]')).not.toBeNull();
    expect(root.querySelector('[data-testid="seq-link"]')).toBeNull();
    expect(root.querySelector('[data-testid="seq-unavailable"]')?.textContent).toContain(ReferenceKeys.Seq.Unavailable);
    expect(root.textContent).toContain(ReferenceKeys.NoEvents);
  });
});
