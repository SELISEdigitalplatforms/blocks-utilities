/**
 * True when the WHATWG URL parser accepts the value as it is — the check zod 3's `.url()` made.
 * zod 4's `z.url()` also trims the value and strips tabs and newlines from it, so schemas that
 * must keep the submitted value untouched use this in a `.refine()` instead.
 */
export const isParsableUrl = (value: string): boolean => {
  try {
    new URL(value);
    return true;
  } catch {
    return false;
  }
};
