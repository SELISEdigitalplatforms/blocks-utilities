import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import { peopleService } from "@blocks-identifier/services/people.service";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import {
  useGetPeople,
  useInvitePeople,
  useResendInvitation,
  useRemoveAccess,
  useRemoveEnvironmentAccess,
  useConfirmInvitation,
  useTransferOwnership,
} from "./use-people";

vi.mock("@blocks-identifier/services/people.service", () => ({
  peopleService: {
    getPeople: vi.fn(),
    invitePeople: vi.fn(),
    resendInvitation: vi.fn(),
    removeAccess: vi.fn(),
    removeEnvironmentAccess: vi.fn(),
    confirmInvitation: vi.fn(),
    transferOwnership: vi.fn(),
  },
}));

vi.mock("@seliseblocks/blocks-kit", () => ({
  useProjectStore: vi.fn(() => ({ selectedTenantGroup: "tg-1" })),
}));

const mutationHooks = [
  ["useInvitePeople", useInvitePeople, "invitePeople"],
  ["useResendInvitation", useResendInvitation, "resendInvitation"],
  ["useRemoveAccess", useRemoveAccess, "removeAccess"],
  ["useRemoveEnvironmentAccess", useRemoveEnvironmentAccess, "removeEnvironmentAccess"],
  ["useConfirmInvitation", useConfirmInvitation, "confirmInvitation"],
  ["useTransferOwnership", useTransferOwnership, "transferOwnership"],
] as const;

describe("use-people hooks", () => {
  beforeEach(() => vi.clearAllMocks());

  it("useGetPeople fetches and selects the response fields", async () => {
    vi.mocked(peopleService.getPeople).mockResolvedValue({
      peoples: [{ id: "1" }],
      totalCount: 1,
      isOwner: true,
    } as never);
    const { result } = renderHook(
      () => useGetPeople({ page: 1, pageSize: 10, filter: "" }),
      { wrapper: createWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual({
      peoples: [{ id: "1" }],
      totalCount: 1,
      isOwner: true,
    });
  });

  it("useGetPeople is disabled without a tenant group", () => {
    vi.mocked(useProjectStore).mockReturnValueOnce({
      selectedTenantGroup: "",
    } as never);
    const { result } = renderHook(
      () => useGetPeople({ page: 1, pageSize: 10, filter: "" }),
      { wrapper: createWrapper() },
    );
    expect(result.current.fetchStatus).toBe("idle");
  });

  it.each(mutationHooks)("%s calls the service", async (_name, hook, method) => {
    vi.mocked(
      peopleService[method as keyof typeof peopleService] as ReturnType<
        typeof vi.fn
      >,
    ).mockResolvedValue({ isSuccess: true } as never);
    const { result } = renderHook(() => hook(), { wrapper: createWrapper() });
    result.current.mutate({} as never);
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(peopleService[method as keyof typeof peopleService]).toHaveBeenCalled();
  });
});
