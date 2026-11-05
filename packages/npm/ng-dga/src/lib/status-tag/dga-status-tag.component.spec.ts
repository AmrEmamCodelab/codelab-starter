import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { DgaStatusTagComponent } from './dga-status-tag.component';

@Component({
  imports: [DgaStatusTagComponent],
  template: `
    <dga-status-tag tone="progress">In review</dga-status-tag>
    <dga-status-tag>Draft</dga-status-tag>
  `,
})
class HostComponent {}

describe('DgaStatusTagComponent', () => {
  it('projects the status text with a decorative dot and the tone', async () => {
    const fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();
    const pills = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.dga-status'));
    expect(pills[0].textContent?.trim()).toBe('In review');
    expect(pills[0].dataset['tone']).toBe('progress');
    expect(pills[0].querySelector('.dga-status__dot')?.getAttribute('aria-hidden')).toBe('true');
    expect(pills[1].dataset['tone']).toBe('neutral');
  });
});
