// @vitest-environment node
import { describe, expect, it } from 'vitest';
import { clipRects, compareImages, diffRatio, padImage, paintRects, withinThreshold } from '../lib/diff.mjs';

const WHITE = { r: 255, g: 255, b: 255, a: 255 };
const BLACK = { r: 0, g: 0, b: 0, a: 255 };
const MASK = { r: 255, g: 0, b: 255, a: 255 };
const PAD = { r: 0, g: 0, b: 0, a: 0 };

function image(width, height, colour = WHITE) {
  const data = new Uint8Array(width * height * 4);
  for (let offset = 0; offset < data.length; offset += 4) data.set([colour.r, colour.g, colour.b, colour.a], offset);
  return { width, height, data };
}

function pixel(img, x, y) {
  const offset = (y * img.width + x) * 4;
  return [...img.data.subarray(offset, offset + 4)];
}

function setPixel(img, x, y, colour) {
  img.data.set([colour.r, colour.g, colour.b, colour.a], (y * img.width + x) * 4);
}

/** A stand-in for pixelmatch: counts pixels whose bytes differ and marks them red in the output. */
function exactMatch(left, right, output, width, height) {
  let count = 0;
  for (let index = 0; index < width * height; index += 1) {
    const offset = index * 4;
    if (left[offset] !== right[offset] || left[offset + 1] !== right[offset + 1] || left[offset + 2] !== right[offset + 2] || left[offset + 3] !== right[offset + 3]) {
      count += 1;
      output.set([255, 0, 0, 255], offset);
    }
  }
  return count;
}

const OPTIONS = { pixelThreshold: 0.1, maskRects: [], maskColour: MASK, paddingColour: PAD, pixelmatch: exactMatch };

describe('clipRects', () => {
  it('snaps outwards to whole pixels, clips to the image and drops empty rectangles', () => {
    expect(
      clipRects(
        [
          { x: 1.5, y: 0.2, width: 2, height: 1 },
          { x: -5, y: -5, width: 7, height: 6 },
          { x: 8, y: 8, width: 10, height: 10 },
          { x: 20, y: 0, width: 5, height: 5 },
          { x: 3, y: 3, width: 0, height: 4 },
        ],
        10,
        10,
      ),
    ).toEqual([
      { x: 1, y: 0, width: 3, height: 2 },
      { x: 0, y: 0, width: 2, height: 1 },
      { x: 8, y: 8, width: 2, height: 2 },
    ]);
  });
});

describe('paintRects and padImage', () => {
  it('paints only inside the rectangles', () => {
    const img = paintRects(image(4, 4), [{ x: 1, y: 1, width: 2, height: 1 }], MASK);
    expect(pixel(img, 1, 1)).toEqual([255, 0, 255, 255]);
    expect(pixel(img, 2, 1)).toEqual([255, 0, 255, 255]);
    expect(pixel(img, 3, 1)).toEqual([255, 255, 255, 255]);
    expect(pixel(img, 1, 2)).toEqual([255, 255, 255, 255]);
  });

  it('pads at the end of each row and below, keeping the original pixels', () => {
    const source = image(2, 1, BLACK);
    const padded = padImage(source, 3, 2, PAD);
    expect(padded.width).toBe(3);
    expect(pixel(padded, 1, 0)).toEqual([0, 0, 0, 255]);
    expect(pixel(padded, 2, 0)).toEqual([0, 0, 0, 0]);
    expect(pixel(padded, 0, 1)).toEqual([0, 0, 0, 0]);
  });

  it('copies an image that already has the size, never aliasing it', () => {
    const source = image(1, 1);
    const copy = padImage(source, 1, 1, PAD);
    copy.data[0] = 1;
    expect(source.data[0]).toBe(255);
  });
});

describe('ratios', () => {
  it('divides by the pixel count, 0 for an empty image', () => {
    expect(diffRatio(5, 100, 50)).toBe(0.001);
    expect(diffRatio(0, 0, 0)).toBe(0);
  });

  it('passes at exactly the threshold and fails above it', () => {
    expect(withinThreshold(0.001, 0.001)).toBe(true);
    expect(withinThreshold(0.0011, 0.001)).toBe(false);
  });
});

describe('compareImages', () => {
  it('reports identical images as 0 % different', () => {
    const result = compareImages(image(10, 10), image(10, 10), OPTIONS);
    expect(result).toMatchObject({ width: 10, height: 10, diffPixels: 0, ratio: 0, sizeMatches: true });
  });

  it('counts changed pixels and passes the pixelmatch options', () => {
    const changed = image(10, 10);
    setPixel(changed, 3, 3, BLACK);
    let seenOptions;
    const result = compareImages(changed, image(10, 10), { ...OPTIONS, pixelmatch: (...args) => { seenOptions = args[5]; return exactMatch(...args); } });
    expect(result.diffPixels).toBe(1);
    expect(result.ratio).toBe(0.01);
    expect(seenOptions).toEqual({ threshold: 0.1, includeAA: false });
    expect(pixel(result.diff, 3, 3)).toEqual([255, 0, 0, 255]);
  });

  it('ignores differences inside masked regions', () => {
    const changed = image(10, 10);
    setPixel(changed, 3, 3, BLACK);
    const result = compareImages(changed, image(10, 10), { ...OPTIONS, maskRects: [{ x: 2.5, y: 2.5, width: 1, height: 1 }] });
    expect(result.diffPixels).toBe(0);
  });

  it('counts the padding of a size mismatch as different', () => {
    const result = compareImages(image(10, 12), image(10, 10), OPTIONS);
    expect(result).toMatchObject({ width: 10, height: 12, diffPixels: 20, sizeMatches: false });
  });
});
