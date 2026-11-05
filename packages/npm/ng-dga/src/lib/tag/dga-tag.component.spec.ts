import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { DgaTagComponent } from './dga-tag.component';

@Component({
  imports: [DgaTagComponent],
  template: `
    <dga-tag tone="success" icon="check">Verified</dga-tag>
    <dga-tag>Plain</dga-tag>
  `,
})
class HostComponent {}

describe('DgaTagComponent', () => {
  it('projects the text, exposes the tone and shows a decorative icon', async () => {
    const fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();
    const tags = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.dga-tag'));
    expect(tags[0].textContent).toContain('Verified');
    expect(tags[0].dataset['tone']).toBe('success');
    expect(tags[0].querySelector('dga-icon')?.getAttribute('aria-hidden')).toBe('true');
    expect(tags[1].dataset['tone']).toBe('neutral');
    expect(tags[1].querySelector('dga-icon')).toBeNull();
  });
});
