import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import { magicUrlService } from "@blocks-utilities/magic-url/services/magic-url.service";
import { toast } from "@/hooks/use-toast";
import { useDeactivateMagicUrl } from "./use-deactivate-magic-url";

vi.mock("@blocks-utilities/magic-url/services/magic-url.service", () => ({
  magicUrlService: { deactivateMagicLinks: vi.fn() },
}));

vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));

describe("useDeactivateMagicUrl", () => {
  beforeEach(() => vi.clearAllMocks());

  it("shows a success toast and calls onSuccess on success", async () => {
    vi.mocked(magicUrlService.deactivateMagicLinks).mockResolvedValue(undefined);
    const onSuccess = vi.fn();
    const { result } = renderHook(() => useDeactivateMagicUrl(), {
      wrapper: createWrapper(),
    });
    result.current.deactivateMagicUrl("id-1", "pk", onSuccess);
    await waitFor(() => expect(onSuccess).toHaveBeenCalled());
    expect(toast).toHaveBeenCalledWith(
      expect.objectContaining({ variant: "success" }),
    );
  });

  it("shows an error toast on failure", async () => {
    vi.mocked(magicUrlService.deactivateMagicLinks).mockRejectedValue(
      new Error("boom"),
    );
    const { result } = renderHook(() => useDeactivateMagicUrl(), {
      wrapper: createWrapper(),
    });
    result.current.deactivateMagicUrl("id-1", "pk");
    await waitFor(() =>
      expect(toast).toHaveBeenCalledWith(
        expect.objectContaining({ variant: "destructive" }),
      ),
    );
  });
});
