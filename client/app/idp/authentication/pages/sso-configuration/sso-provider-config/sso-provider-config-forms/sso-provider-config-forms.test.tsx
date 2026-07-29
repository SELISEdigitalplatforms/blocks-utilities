import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { SsoProviderConfigForms } from "./sso-provider-config-forms";

const navigate = vi.fn();
vi.mock("react-router", async () => {
  const actual =
    await vi.importActual<typeof import("react-router")>(
      "react-router",
    );
  return { ...actual, useNavigate: () => navigate };
});

const saveSso = vi.fn();
vi.mock("@blocks-idp/authentication/hooks/use-sso", () => ({
  useGetSsoCredentialById: () => ({ data: null }),
  useSaveSsoCredential: () => ({ mutateAsync: saveSso }),
}));

const showSuccessToast = vi.fn();
const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: (...a: unknown[]) => showSuccessToast(...a),
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const sampleData = {
  audience: "aud",
  clientId: "cid",
  clientSecret: "secret",
  userPermissions: [{ resource: "res-1" }],
  userRoles: [{ slug: "role-1" }],
  provider: "google",
  redirectUrl: "https://cb.test",
};

// Each provider form receives a `save` callback; the Google mock exposes it.
vi.mock("./sso-provider-config-google-form", () => ({
  SSOProviderConfigGoogleForm: ({ save }: { save: (d: unknown) => void }) => (
    <button onClick={() => save(sampleData)}>google-save</button>
  ),
}));
vi.mock("./sso-provider-config-github-form", () => ({
  SSOProviderConfigGithubForm: () => <div>github-form</div>,
}));
vi.mock("./sso-provider-config-linkedin-form", () => ({
  SSOProviderConfigLinkedINForm: () => <div>linkedin-form</div>,
}));
vi.mock("./sso-provider-config-microsoft-form", () => ({
  SSOProviderConfigMicrosoftForm: () => <div>microsoft-form</div>,
}));
vi.mock("./sso-provider-config-x-form", () => ({
  SSOProviderConfigXForm: () => <div>x-form</div>,
}));
vi.mock("./sso-provider-config-blocks-own-sso-form", () => ({
  SSOProviderConfigOwnSSOForm: () => <div>ownsso-form</div>,
}));

const renderForms = (provider: string, id = "") =>
  render(
    <MemoryRouter>
      <SsoProviderConfigForms provider={provider as never} id={id} />
    </MemoryRouter>,
  );

describe("SsoProviderConfigForms", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useProjectStore.setState({ selectedProject: { tenantId: "tg-1" } });
  });

  it("renders the form matching the provider", () => {
    renderForms("github");
    expect(screen.getByText("github-form")).toBeInTheDocument();
  });

  it("renders nothing for an unknown provider", () => {
    const { container } = renderForms("unknown");
    expect(container.firstChild).toBeNull();
  });

  it("saves the configuration and navigates for a new credential", async () => {
    saveSso.mockResolvedValue({ isSuccess: true, itemId: "new-1" });
    const user = userEvent.setup();
    renderForms("google", "");

    await user.click(screen.getByText("google-save"));

    await waitFor(() =>
      expect(saveSso).toHaveBeenCalledWith(
        expect.objectContaining({
          provider: "google",
          projectKey: "tg-1",
          initialPermissions: ["res-1"],
          initialRoles: ["role-1"],
        }),
      ),
    );
    expect(navigate).toHaveBeenCalledWith(
      expect.stringContaining("id=new-1"),
    );
    expect(showSuccessToast).toHaveBeenCalled();
  });

  it("shows an error toast when the save is unsuccessful", async () => {
    saveSso.mockResolvedValue({ isSuccess: false, errors: "boom" });
    const user = userEvent.setup();
    renderForms("google", "existing-1");

    await user.click(screen.getByText("google-save"));

    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "boom" }),
    );
    expect(navigate).not.toHaveBeenCalled();
  });
});
