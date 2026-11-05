// Image comparison of e2e/visual.mjs, pure: images are { width, height, data } with RGBA bytes (pngjs's shape), and
// pixelmatch is passed in, so the logic is unit-tested without a browser or native module.

const CHANNELS = 4;
const RED = 0;
const GREEN = 1;
const BLUE = 2;
const ALPHA = 3;

/**
 * Mask rectangles (from getBoundingClientRect, page coordinates, possibly fractional or off-page) clipped to an image
 * and snapped outwards to whole pixels. Empty rectangles are dropped.
 */
export function clipRects(rects, width, height) {
  return rects
    .map((rect) => {
      const left = Math.max(0, Math.floor(rect.x));
      const top = Math.max(0, Math.floor(rect.y));
      const right = Math.min(width, Math.ceil(rect.x + rect.width));
      const bottom = Math.min(height, Math.ceil(rect.y + rect.height));
      return { x: left, y: top, width: right - left, height: bottom - top };
    })
    .filter((rect) => rect.width > 0 && rect.height > 0);
}

/** Paints the rectangles (already clipped) with an opaque colour, in place. */
export function paintRects(image, rects, colour) {
  for (const rect of rects) {
    for (let y = rect.y; y < rect.y + rect.height; y += 1) {
      for (let x = rect.x; x < rect.x + rect.width; x += 1) {
        const offset = (y * image.width + x) * CHANNELS;
        image.data[offset + RED] = colour.r;
        image.data[offset + GREEN] = colour.g;
        image.data[offset + BLUE] = colour.b;
        image.data[offset + ALPHA] = colour.a;
      }
    }
  }
  return image;
}

/** A copy of the image on a larger canvas (top-left aligned), the new area filled with the padding colour. */
export function padImage(image, width, height, colour) {
  if (image.width === width && image.height === height) return { width, height, data: Uint8Array.from(image.data) };
  const data = new Uint8Array(width * height * CHANNELS);
  for (let offset = 0; offset < data.length; offset += CHANNELS) {
    data[offset + RED] = colour.r;
    data[offset + GREEN] = colour.g;
    data[offset + BLUE] = colour.b;
    data[offset + ALPHA] = colour.a;
  }
  const rowBytes = image.width * CHANNELS;
  for (let y = 0; y < image.height; y += 1) {
    data.set(image.data.subarray(y * rowBytes, (y + 1) * rowBytes), y * width * CHANNELS);
  }
  return { width, height, data };
}

/** Share of differing pixels, 0 for an empty image. */
export function diffRatio(diffPixels, width, height) {
  const total = width * height;
  return total === 0 ? 0 : diffPixels / total;
}

/** True when the ratio is within the allowed maximum (inclusive). */
export function withinThreshold(ratio, maxDiffRatio) {
  return ratio <= maxDiffRatio;
}

/**
 * Compares two images: both are padded to the larger size (the padding counts as different), the mask rectangles are
 * painted in both, then pixelmatch counts differing pixels with anti-aliasing ignored.
 * Returns { width, height, diffPixels, ratio, sizeMatches, diff (RGBA image of the differences) }.
 */
export function compareImages(actual, expected, { pixelThreshold, maskRects, maskColour, paddingColour, pixelmatch }) {
  const width = Math.max(actual.width, expected.width);
  const height = Math.max(actual.height, expected.height);
  const rects = clipRects(maskRects, width, height);
  const left = paintRects(padImage(actual, width, height, paddingColour), rects, maskColour);
  const right = paintRects(padImage(expected, width, height, paddingColour), rects, maskColour);
  const diff = { width, height, data: new Uint8Array(width * height * CHANNELS) };
  const diffPixels = pixelmatch(left.data, right.data, diff.data, width, height, { threshold: pixelThreshold, includeAA: false });
  return {
    width,
    height,
    diffPixels,
    ratio: diffRatio(diffPixels, width, height),
    sizeMatches: actual.width === expected.width && actual.height === expected.height,
    diff,
  };
}
