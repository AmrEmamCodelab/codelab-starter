import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** The surface a hero sits on when it has no image. */
export const DgaHeroVariant = {
  /** The muted page surface with dark text. */
  Plain: 'plain',
  /** The brand primary colour with light text. */
  Brand: 'brand',
} as const;
export type DgaHeroVariant = (typeof DgaHeroVariant)[keyof typeof DgaHeroVariant];

/**
 * The DGA hero: the large introduction band at the top of a portal page. Everything it says is projected, already
 * translated: `[dgaHeroTitle]` (bring your own heading element, usually the page's `<h1>`), `[dgaHeroSubtitle]`,
 * free content, and `[dgaHeroActions]` (buttons or links). An optional `imageSrc` fills the band behind a dark overlay
 * that keeps the text readable; the image is decorative (`alt=""`), so the projected text must carry the meaning.
 *
 * @example
 * <dga-hero variant="brand" imageSrc="assets/hero.webp">
 *   <h1 dgaHeroTitle>{{ 'portal.hero.title' | t }}</h1>
 *   <p dgaHeroSubtitle>{{ 'portal.hero.subtitle' | t }}</p>
 *   <dga-button dgaHeroActions variant="secondary-solid">{{ 'portal.hero.start' | t }}</dga-button>
 * </dga-hero>
 */
@Component({
  selector: 'dga-hero',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-hero.component.html',
  styleUrl: './dga-hero.component.css',
  host: {
    class: 'dga-hero',
    '[attr.data-variant]': 'variant()',
    '[class.dga-hero--image]': '!!imageSrc()',
  },
})
export class DgaHeroComponent {
  readonly variant = input<DgaHeroVariant>(DgaHeroVariant.Plain);
  /** A decorative background image served by the app; null or empty for none. */
  readonly imageSrc = input<string | null>(null);
}
