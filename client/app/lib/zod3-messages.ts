/**
 * zod 3's default validation messages, word for word.
 *
 * zod 4 words its defaults differently ("Too small: expected string to have >=1 characters"), so a
 * check whose failure a user can see passes one of these explicitly. That keeps every form reading
 * exactly as it did before the zod 4 upgrade.
 */
export const zod3Messages = {
  stringMin: (minimum: number) => `String must contain at least ${minimum} character(s)`,
  stringMax: (maximum: number) => `String must contain at most ${maximum} character(s)`,
  arrayMin: (minimum: number) => `Array must contain at least ${minimum} element(s)`,
  arrayMax: (maximum: number) => `Array must contain at most ${maximum} element(s)`,
  numberMin: (minimum: number) => `Number must be greater than or equal to ${minimum}`,
  numberMax: (maximum: number) => `Number must be less than or equal to ${maximum}`,
  numberPositive: "Number must be greater than 0",
  /** `.int()`'s message. zod 4's `.int()` behaves differently, so callers use `Number.isInteger`. */
  integer: "Expected integer, received float",
  /** What zod 3 said when a coerced number came out as NaN. */
  notANumber: "Expected number, received nan",
} as const;
