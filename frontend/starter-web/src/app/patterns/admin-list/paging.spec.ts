import { describe, expect, it } from 'vitest';
import { DEFAULT_PAGE_SIZE, PAGE_GAP, clampPage, pageCount, pageRange, pagerItems } from './paging';

describe('paging', () => {
  describe('pageCount', () => {
    it('rounds up and is at least 1', () => {
      expect(pageCount(95, 20)).toBe(5);
      expect(pageCount(100, 20)).toBe(5);
      expect(pageCount(101, 20)).toBe(6);
      expect(pageCount(0, 20)).toBe(1);
    });

    it('treats a broken total or page size safely', () => {
      expect(pageCount(-5, 20)).toBe(1);
      expect(pageCount(Number.NaN, 20)).toBe(1);
      expect(pageCount(45, 0)).toBe(Math.ceil(45 / DEFAULT_PAGE_SIZE));
      expect(pageCount(45, -3)).toBe(Math.ceil(45 / DEFAULT_PAGE_SIZE));
    });
  });

  describe('clampPage', () => {
    it('keeps the page inside 1..pageCount', () => {
      expect(clampPage(3, 95, 20)).toBe(3);
      expect(clampPage(9, 95, 20)).toBe(5);
      expect(clampPage(0, 95, 20)).toBe(1);
      expect(clampPage(2.7, 95, 20)).toBe(2);
      expect(clampPage(4, 0, 20)).toBe(1);
    });
  });

  describe('pageRange', () => {
    it('gives the 1-based rows of the page', () => {
      expect(pageRange(1, 95, 20)).toEqual({ from: 1, to: 20 });
      expect(pageRange(2, 95, 20)).toEqual({ from: 21, to: 40 });
      expect(pageRange(5, 95, 20)).toEqual({ from: 81, to: 95 });
    });

    it('is 0–0 without rows and clamps a stale page', () => {
      expect(pageRange(1, 0, 20)).toEqual({ from: 0, to: 0 });
      expect(pageRange(7, 30, 20)).toEqual({ from: 21, to: 30 });
    });
  });

  describe('pagerItems', () => {
    it('shows every page when they fit', () => {
      expect(pagerItems(1, 1)).toEqual([1]);
      expect(pagerItems(2, 5)).toEqual([1, 2, 3, 4, 5]);
      expect(pagerItems(4, 7)).toEqual([1, 2, 3, 4, 5, 6, 7]);
    });

    it('puts gaps around the window, with the first and last page always shown', () => {
      expect(pagerItems(10, 20)).toEqual([1, PAGE_GAP, 8, 9, 10, 11, 12, PAGE_GAP, 20]);
      expect(pagerItems(1, 20)).toEqual([1, 2, 3, 4, 5, PAGE_GAP, 20]);
      expect(pagerItems(20, 20)).toEqual([1, PAGE_GAP, 16, 17, 18, 19, 20]);
    });

    it('never uses a gap for a single skipped page', () => {
      expect(pagerItems(5, 9)).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9]);
      expect(pagerItems(5, 10)).toEqual([1, 2, 3, 4, 5, 6, 7, PAGE_GAP, 10]);
    });

    it('clamps the page into the count', () => {
      expect(pagerItems(50, 3)).toEqual([1, 2, 3]);
      expect(pagerItems(0, 0)).toEqual([1]);
    });
  });
});
