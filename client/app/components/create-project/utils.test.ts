import { beforeEach, describe, expect, it } from "vitest";
import { useCreateProjectFormState, shortGuidGenerator } from "./utils";

describe("useCreateProjectFormState", () => {
  beforeEach(() => useCreateProjectFormState.getState().resetFormData());

  it("starts with three default form sections", () => {
    expect(useCreateProjectFormState.getState().formData).toHaveLength(3);
  });

  it("setFormData replaces a section by index", () => {
    useCreateProjectFormState
      .getState()
      .setFormData(0, {
        name: "Proj",
        isAcceptBlocksTerms: true,
        isUseBlocksExclusively: true,
      });
    expect(useCreateProjectFormState.getState().formData[0].name).toBe("Proj");
  });

  it("resetFormData restores defaults", () => {
    useCreateProjectFormState
      .getState()
      .setFormData(0, {
        name: "X",
        isAcceptBlocksTerms: true,
        isUseBlocksExclusively: true,
      });
    useCreateProjectFormState.getState().resetFormData();
    expect(useCreateProjectFormState.getState().formData[0].name).toBe("");
  });
});

describe("shortGuidGenerator", () => {
  it("produces a lowercase string of the requested length", () => {
    const guid = shortGuidGenerator(5);
    expect(guid).toHaveLength(5);
    expect(guid).toMatch(/^[a-z]{5}$/);
  });

  it("produces different values across calls", () => {
    expect(shortGuidGenerator(10)).not.toBe(shortGuidGenerator(10));
  });
});
