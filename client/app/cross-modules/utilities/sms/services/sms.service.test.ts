import { HttpError } from "@seliseblocks/genesis-os/lib";
import { beforeEach, describe, expect, it, vi } from "vitest";

const http = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), delete: vi.fn() }));

vi.mock("@/lib/http-client", () => ({ serviceInstances: { utitlitiesService: http } }));

import { SmsRequestError, smsService } from "./sms.service";

describe("smsService", () => {
  beforeEach(() => {
    http.get.mockReset();
    http.post.mockReset();
    http.delete.mockReset();
  });

  it("reads a missing configuration (404) as none, not as a failure", async () => {
    http.get.mockRejectedValue(new HttpError(404, { errors: { Configuration: "No active SMS provider configuration was found." } }));

    await expect(smsService.getProviderConfiguration()).resolves.toBeNull();
  });

  it("keeps the server's field errors on a refused save", async () => {
    http.post.mockRejectedValue(new HttpError(400, { errors: { SenderName: "Too long.", Tenant: "No tenant." } }));

    const error = await smsService
      .saveProviderConfiguration({} as never)
      .catch((thrown: unknown) => thrown);

    expect(error).toBeInstanceOf(SmsRequestError);
    expect((error as SmsRequestError).fieldErrors).toEqual({ SenderName: "Too long.", Tenant: "No tenant." });
    expect((error as SmsRequestError).status).toBe(400);
  });

  it("sends template filters only when set, and encodes ids", async () => {
    http.get.mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 20 });
    http.delete.mockResolvedValue({ isSuccess: true, errors: {} });

    await smsService.getTemplates({ page: 2, pageSize: 20, search: " otp ", language: "" });
    await smsService.deleteTemplate("a/b");

    expect(http.get).toHaveBeenCalledWith("/api/Sms/GetTemplates?page=2&pageSize=20&search=otp");
    expect(http.delete).toHaveBeenCalledWith("/api/Sms/DeleteTemplate?templateId=a%2Fb");
  });
});
