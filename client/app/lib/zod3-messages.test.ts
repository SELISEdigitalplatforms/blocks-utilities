import { describe, expect, it } from "vitest";
import { z } from "zod";
import { zod3Messages } from "./zod3-messages";

const firstMessage = (schema: z.ZodType, value: unknown) =>
  schema.safeParse(value).error?.issues[0]?.message;

describe("zod3Messages", () => {
  it("words string and array lengths as zod 3 did", () => {
    expect(firstMessage(z.string().min(5, zod3Messages.stringMin(5)), "1234")).toBe(
      "String must contain at least 5 character(s)",
    );
    expect(firstMessage(z.string().max(2, zod3Messages.stringMax(2)), "abc")).toBe(
      "String must contain at most 2 character(s)",
    );
    expect(firstMessage(z.array(z.string()).min(1, zod3Messages.arrayMin(1)), [])).toBe(
      "Array must contain at least 1 element(s)",
    );
    expect(zod3Messages.arrayMax(50)).toBe("Array must contain at most 50 element(s)");
  });

  it("words number bounds as zod 3 did", () => {
    expect(firstMessage(z.number().min(0, zod3Messages.numberMin(0)), -1)).toBe(
      "Number must be greater than or equal to 0",
    );
    expect(firstMessage(z.number().max(3650, zod3Messages.numberMax(3650)), 3651)).toBe(
      "Number must be less than or equal to 3650",
    );
    expect(firstMessage(z.number().positive(zod3Messages.numberPositive), 0)).toBe(
      "Number must be greater than 0",
    );
  });

  it("words a fraction and a NaN as zod 3 did", () => {
    expect(firstMessage(z.number().refine(Number.isInteger, zod3Messages.integer), 1.5)).toBe(
      "Expected integer, received float",
    );
    expect(firstMessage(z.coerce.number({ error: zod3Messages.notANumber }), "abc")).toBe(
      "Expected number, received nan",
    );
  });
});
