import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  cn,
  formatDate,
  formatFullDate,
  parseDateString,
  compareDates,
  clearBreadCrumbTitleEntry,
  BREADCRUMB_CUSTOM_TITLES,
  debounce,
  parseMongoDBString,
  checkValidDate,
  deepEqual,
  clearQueryString,
  getUniqueID,
  formatSize,
} from "./utils";

describe("cn", () => {
  it("merges class names and dedupes tailwind conflicts", () => {
    expect(cn("px-2", "px-4")).toBe("px-4");
    expect(cn("a", false && "b", "c")).toBe("a c");
  });
});

describe("formatDate", () => {
  const date = new Date(2023, 0, 5, 9, 7); // 05/01/2023, 09:07

  it("formats with time by default", () => {
    expect(formatDate(date)).toBe("05/01/2023, 09:07");
  });

  it("omits time when withoutTime is true", () => {
    expect(formatDate(date, true)).toBe("05/01/2023");
  });
});

describe("formatFullDate", () => {
  const date = new Date(2023, 6, 5, 14, 3); // Jul 05, 2023 at 14:03

  it("formats with month name and time", () => {
    expect(formatFullDate(date)).toBe("Jul 05, 2023 at 14:03");
  });

  it("omits time when requested", () => {
    expect(formatFullDate(date, true)).toBe("Jul 05, 2023");
  });
});

describe("parseDateString", () => {
  it("parses an ISO string into a Date", () => {
    const d = parseDateString("2023-01-01T00:00:00Z");
    expect(d).toBeInstanceOf(Date);
    expect(d.getUTCFullYear()).toBe(2023);
  });
});

describe("compareDates", () => {
  it("returns negative when first is earlier", () => {
    expect(compareDates("2023-01-01", "2023-01-02")).toBeLessThan(0);
  });

  it("returns positive when first is later", () => {
    expect(compareDates("2023-01-03", "2023-01-02")).toBeGreaterThan(0);
  });

  it("returns zero when equal", () => {
    expect(compareDates("2023-01-02", "2023-01-02")).toBe(0);
  });
});

describe("clearBreadCrumbTitleEntry", () => {
  it("nulls the entry for the given path", () => {
    BREADCRUMB_CUSTOM_TITLES["/x"] = "X";
    clearBreadCrumbTitleEntry("/x");
    expect(BREADCRUMB_CUSTOM_TITLES["/x"]).toBeNull();
  });
});

describe("debounce", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("invokes the function only after the delay", () => {
    const fn = vi.fn();
    const debounced = debounce(fn, 200);
    debounced();
    debounced();
    expect(fn).not.toHaveBeenCalled();
    vi.advanceTimersByTime(200);
    expect(fn).toHaveBeenCalledTimes(1);
  });

  it("cancel prevents a pending invocation", () => {
    const fn = vi.fn();
    const debounced = debounce(fn, 200);
    debounced();
    debounced.cancel();
    vi.advanceTimersByTime(200);
    expect(fn).not.toHaveBeenCalled();
  });

  it("forwards arguments and this binding", () => {
    const fn = vi.fn();
    const debounced = debounce(fn, 100);
    debounced("a", 1);
    vi.advanceTimersByTime(100);
    expect(fn).toHaveBeenCalledWith("a", 1);
  });
});

describe("parseMongoDBString", () => {
  it("unwraps ISODate and ObjectId", () => {
    expect(parseMongoDBString('ObjectId("abc")')).toBe('"abc"');
    expect(parseMongoDBString('ISODate("2023-01-01")')).toBe('"2023-01-01"');
  });

  it("unwraps $date objects and NumberLong", () => {
    expect(parseMongoDBString('{ "$date": "2023-01-01" }')).toBe('"2023-01-01"');
    expect(parseMongoDBString("NumberLong(42)")).toBe("42");
  });
});

describe("checkValidDate", () => {
  it("returns true for a valid modern date", () => {
    expect(checkValidDate("2023-05-01")).toBe(true);
  });

  it("returns false for an invalid date", () => {
    expect(checkValidDate("not-a-date")).toBe(false);
  });

  it("returns false for dates before 1900", () => {
    expect(checkValidDate("1800-01-01")).toBe(false);
  });
});

describe("deepEqual", () => {
  it("returns true for structurally equal objects", () => {
    expect(deepEqual({ a: 1, b: { c: 2 } }, { a: 1, b: { c: 2 } })).toBe(true);
  });

  it("returns false when keys differ", () => {
    expect(deepEqual({ a: 1 }, { a: 1, b: 2 })).toBe(false);
    expect(deepEqual({ a: 1 }, { b: 1 })).toBe(false);
  });

  it("returns false when values differ", () => {
    expect(deepEqual({ a: 1 }, { a: 2 })).toBe(false);
  });

  it("handles primitives and null", () => {
    expect(deepEqual(1, 1)).toBe(true);
    expect(deepEqual(null, {})).toBe(false);
    expect(deepEqual(1, "1")).toBe(false);
  });
});

describe("clearQueryString", () => {
  beforeEach(() => {
    window.history.replaceState(null, "", "/page?a=1&b=2&c=3");
  });

  it("removes all query params by default", () => {
    clearQueryString();
    expect(window.location.search).toBe("");
  });

  it("keeps params listed in except", () => {
    clearQueryString({ except: ["b"] });
    expect(window.location.search).toBe("?b=2");
  });
});

describe("getUniqueID", () => {
  it("produces a BLK-prefixed id with 6 trailing letters", () => {
    const id = getUniqueID();
    expect(id).toMatch(/^BLK-\d+-[A-Z]{6}$/);
  });

  it("produces distinct ids across calls", () => {
    expect(getUniqueID()).not.toBe(getUniqueID());
  });
});

describe("formatSize", () => {
  it("formats bytes with the smallest fitting unit", () => {
    expect(formatSize(500)).toBe("500 B");
    expect(formatSize(1024)).toBe("1 KB");
    expect(formatSize(1024 * 1024)).toBe("1 MB");
  });

  it("respects the input unit", () => {
    expect(formatSize(1, "KB")).toBe("1 KB");
    expect(formatSize(1024, "KB")).toBe("1 MB");
  });

  it("honors the decimals argument", () => {
    expect(formatSize(1536, "B", 1)).toBe("1.5 KB");
  });
});
