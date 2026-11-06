import { TestBed } from '@angular/core/testing';
import { firstValueFrom, of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { ApiService } from '../../../core/api/api.service';
import { SKIP_ERROR_DIALOG, SKIP_SESSION_EXPIRY } from '../../../core/http/http-context';
import { PortalContentClient, PortalContentPages, portalContentPath, type PortalContentDto } from './portal-content.client';

describe('PortalContentClient', () => {
  it.each(Object.values(PortalContentPages))('builds the content path of %s', (page) => {
    expect(portalContentPath(page)).toBe(`api/portal/content/${page}`);
  });

  it('reads a page anonymously and self-handled', async () => {
    const dto: PortalContentDto = { page: PortalContentPages.Faq, updatedAt: null, sections: [] };
    const get = vi.fn().mockReturnValue(of(dto));
    TestBed.configureTestingModule({ providers: [{ provide: ApiService, useValue: { get } }] });

    expect(await firstValueFrom(TestBed.inject(PortalContentClient).page(PortalContentPages.Faq))).toBe(dto);

    const [path, options] = get.mock.calls[0] as [string, { anonymous: boolean; context: { get: (token: unknown) => unknown } }];
    expect(path).toBe('api/portal/content/faq');
    expect(options.anonymous).toBe(true);
    expect(options.context.get(SKIP_ERROR_DIALOG)).toBe(true);
    expect(options.context.get(SKIP_SESSION_EXPIRY)).toBe(true);
  });
});
