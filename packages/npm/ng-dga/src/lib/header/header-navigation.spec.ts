import { describe, expect, it } from 'vitest';
import { DgaHeaderKey, exactDgaNavLinks, hasDgaNavChildren, nextDgaMenuIndex, opensDgaSubmenu } from './header-navigation';

describe('header navigation', () => {
  it('knows which entries open a submenu', () => {
    expect(hasDgaNavChildren({ id: 'a', label: 'A', children: [{ id: 'b', label: 'B', link: '/b' }] })).toBe(true);
    expect(hasDgaNavChildren({ id: 'a', label: 'A', children: [] })).toBe(false);
    expect(hasDgaNavChildren({ id: 'a', label: 'A', link: '/a' })).toBe(false);
  });

  it('opens a submenu on ArrowDown, Enter and Space only', () => {
    expect(opensDgaSubmenu(DgaHeaderKey.ArrowDown)).toBe(true);
    expect(opensDgaSubmenu(DgaHeaderKey.Enter)).toBe(true);
    expect(opensDgaSubmenu(DgaHeaderKey.Space)).toBe(true);
    expect(opensDgaSubmenu(DgaHeaderKey.ArrowUp)).toBe(false);
    expect(opensDgaSubmenu(DgaHeaderKey.Escape)).toBe(false);
  });

  it('moves down and up, wrapping at both ends', () => {
    expect(nextDgaMenuIndex(3, 0, DgaHeaderKey.ArrowDown)).toBe(1);
    expect(nextDgaMenuIndex(3, 2, DgaHeaderKey.ArrowDown)).toBe(0);
    expect(nextDgaMenuIndex(3, 0, DgaHeaderKey.ArrowUp)).toBe(2);
    expect(nextDgaMenuIndex(3, 2, DgaHeaderKey.ArrowUp)).toBe(1);
  });

  it('starts from outside the list: down to the first link, up to the last', () => {
    expect(nextDgaMenuIndex(3, -1, DgaHeaderKey.ArrowDown)).toBe(0);
    expect(nextDgaMenuIndex(3, -1, DgaHeaderKey.ArrowUp)).toBe(2);
  });

  it('jumps to the ends and ignores other keys and empty lists', () => {
    expect(nextDgaMenuIndex(4, 2, DgaHeaderKey.Home)).toBe(0);
    expect(nextDgaMenuIndex(4, 0, DgaHeaderKey.End)).toBe(3);
    expect(nextDgaMenuIndex(4, 0, DgaHeaderKey.Tab)).toBeNull();
    expect(nextDgaMenuIndex(0, -1, DgaHeaderKey.ArrowDown)).toBeNull();
  });
});

describe('exactDgaNavLinks', () => {
  it('makes a link exact when another entry, at any depth, links below it', () => {
    const exact = exactDgaNavLinks([
      { id: 'home', label: 'Home', link: '/portal' },
      { id: 'faq', label: 'FAQ', link: '/portal/faq' },
      { id: 'more', label: 'More', children: [{ id: 'guide', label: 'Guide', link: '/portal/guide/' }] },
      { id: 'about', label: 'About', link: '/about' },
    ]);
    expect([...exact]).toEqual(['/portal']);
  });

  it('treats the root link as a parent of every other link', () => {
    expect([...exactDgaNavLinks([{ id: 'r', label: 'R', link: '/' }, { id: 'a', label: 'A', link: '/a' }])]).toEqual(['/']);
  });

  it('does not treat a sibling with a shared name prefix as below', () => {
    expect(exactDgaNavLinks([{ id: 'a', label: 'A', link: '/port' }, { id: 'b', label: 'B', link: '/portal' }]).size).toBe(0);
  });

  it('follows an explicit exact flag over the default', () => {
    const exact = exactDgaNavLinks([
      { id: 'home', label: 'Home', link: '/portal', exact: false },
      { id: 'faq', label: 'FAQ', link: '/portal/faq' },
      { id: 'solo', label: 'Solo', link: '/solo', exact: true },
    ]);
    expect([...exact]).toEqual(['/solo']);
  });
});
