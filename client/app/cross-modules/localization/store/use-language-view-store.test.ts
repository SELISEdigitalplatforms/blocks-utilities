import { beforeEach, describe, expect, it } from "vitest";
import { useLanguageViewStore } from "./use-language-view-store";

describe("useLanguageViewStore", () => {
  beforeEach(() => {
    useLanguageViewStore.getState().resetSelectedLanguages();
  });

  it("setSelectedLanguages replaces the list", () => {
    useLanguageViewStore.getState().setSelectedLanguages(["en", "de"]);
    expect(useLanguageViewStore.getState().selectedLanguages).toEqual([
      "en",
      "de",
    ]);
  });

  it("toggleLanguage adds then removes a code", () => {
    useLanguageViewStore.getState().toggleLanguage("en");
    expect(useLanguageViewStore.getState().selectedLanguages).toContain("en");
    useLanguageViewStore.getState().toggleLanguage("en");
    expect(useLanguageViewStore.getState().selectedLanguages).not.toContain("en");
  });

  it("setSelectedOptionalColumns replaces the columns", () => {
    useLanguageViewStore.getState().setSelectedOptionalColumns(["createdBy"]);
    expect(useLanguageViewStore.getState().selectedOptionalColumns).toEqual([
      "createdBy",
    ]);
  });

  it("toggleOptionalColumn adds then removes a column", () => {
    useLanguageViewStore.getState().toggleOptionalColumn("createdBy");
    expect(useLanguageViewStore.getState().selectedOptionalColumns).toContain(
      "createdBy",
    );
    useLanguageViewStore.getState().toggleOptionalColumn("createdBy");
    expect(
      useLanguageViewStore.getState().selectedOptionalColumns,
    ).not.toContain("createdBy");
  });

  it("resetSelectedLanguages clears both lists", () => {
    useLanguageViewStore.getState().setSelectedLanguages(["en"]);
    useLanguageViewStore.getState().setSelectedOptionalColumns(["c"]);
    useLanguageViewStore.getState().resetSelectedLanguages();
    expect(useLanguageViewStore.getState().selectedLanguages).toEqual([]);
    expect(useLanguageViewStore.getState().selectedOptionalColumns).toEqual([]);
  });
});
