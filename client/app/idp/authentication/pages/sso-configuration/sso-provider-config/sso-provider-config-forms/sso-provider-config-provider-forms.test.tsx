import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import type { ReactElement } from "react";

import { useProjectStore } from "@seliseblocks/genesis-os";
import { createWrapper } from "@/test-utils/test-providers/query-client";

import { SSOProviderConfigGoogleForm } from "./sso-provider-config-google-form";
import { SSOProviderConfigGithubForm } from "./sso-provider-config-github-form";
import { SSOProviderConfigMicrosoftForm } from "./sso-provider-config-microsoft-form";
import { SSOProviderConfigLinkedINForm } from "./sso-provider-config-linkedin-form";
import { SSOProviderConfigXForm } from "./sso-provider-config-x-form";
import { SSOProviderConfigOwnSSOForm } from "./sso-provider-config-blocks-own-sso-form";
import { SSOProviderConfigBlocksForm } from "./sso-provider-config-blocks-form";

// The Add-role / Add-permission children fetch through react-query. Return
// empty, non-loading data so the whole form tree mounts deterministically.
vi.mock("@blocks-idp/iam/hooks/use-roles", () => ({
  useGetRoles: () => ({ data: { data: [], totalCount: 0 }, isLoading: false }),
}));
vi.mock("@blocks-idp/iam/hooks/use-permission", () => ({
  useGetPermissions: () => ({ data: { data: [], totalCount: 0 }, isLoading: false }),
}));

const saveSsoCredential = vi.fn();
const saveOidcCredential = vi.fn();
const oidcExisting = { data: undefined as unknown };
vi.mock("@blocks-idp/authentication/hooks/use-sso", () => ({
  useSaveSsoCredential: () => ({ mutateAsync: saveSsoCredential }),
  useSaveOIDCCredential: () => ({ mutateAsync: saveOidcCredential }),
  useSaveGetOIDCCredential: () => ({ data: oidcExisting.data }),
}));

const showSuccessToast = vi.fn();
const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: (...a: unknown[]) => showSuccessToast(...a),
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const renderForm = (node: ReactElement) => {
  const Wrapper = createWrapper();
  return render(
    <Wrapper>
      <MemoryRouter>{node}</MemoryRouter>
    </Wrapper>,
  );
};

const oauthConfig = {
  provider: "google",
  audience: "https://aud.test",
  clientId: "cid",
  clientSecret: "secret",
  redirectUrl: "https://cb.test",
  initialRoles: [],
  initialPermissions: [],
  userRoles: [{ name: "user", slug: "user", description: "default role", itemId: "1234" }],
  userPermissions: [],
} as never;

describe("SSO OAuth provider forms", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useProjectStore.setState({ selectedProject: { tenantId: "tenant-1" } as never });
  });

  const oauthForms: Array<[string, ReactElement]> = [
    ["Google", <SSOProviderConfigGoogleForm key="google" save={vi.fn()} configuration={null} />],
    ["Github", <SSOProviderConfigGithubForm key="github" save={vi.fn()} configuration={null} />],
    ["Microsoft", <SSOProviderConfigMicrosoftForm key="microsoft" save={vi.fn()} configuration={null} />],
    ["LinkedIn", <SSOProviderConfigLinkedINForm key="linkedin" save={vi.fn()} configuration={null} />],
    ["X", <SSOProviderConfigXForm key="x" save={vi.fn()} configuration={null} />],
  ];

  it.each(oauthForms)("renders the %s form scaffold", (_label, node) => {
    renderForm(node);
    expect(screen.getByText("General")).toBeInTheDocument();
    expect(screen.getAllByText("Roles").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Permissions").length).toBeGreaterThan(0);
    expect(screen.getByText("Client ID")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Save" })).toBeInTheDocument();
  });

  it("calls save with a valid Google configuration on submit", async () => {
    const save = vi.fn();
    const user = userEvent.setup();
    renderForm(<SSOProviderConfigGoogleForm save={save} configuration={oauthConfig} />);

    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(save).toHaveBeenCalledTimes(1));
    expect(save.mock.calls[0][0]).toEqual(
      expect.objectContaining({ provider: "google", clientId: "cid" }),
    );
  });

  it("does not call save when required fields are invalid", async () => {
    const save = vi.fn();
    const user = userEvent.setup();
    renderForm(<SSOProviderConfigGithubForm save={save} configuration={null} />);

    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(screen.getByText("Client id is required")).toBeInTheDocument());
    expect(save).not.toHaveBeenCalled();
  });
});

describe("SSOProviderConfigOwnSSOForm", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useProjectStore.setState({ selectedProject: { tenantId: "tenant-1" } as never });
  });

  const fillOwnSso = async (user: ReturnType<typeof userEvent.setup>) => {
    const inputs = screen.getAllByRole("textbox");
    // provider (disabled), clientId, redirectUrl, audience, wellKnownUrl
    await user.type(screen.getByRole("textbox", { name: /Client ID/i }), "cid");
    await user.type(screen.getByRole("textbox", { name: /Redirect Url/i }), "https://cb.test");
    await user.type(screen.getByRole("textbox", { name: /Audience/i }), "https://aud.test");
    await user.type(screen.getByRole("textbox", { name: /Well Known URL/i }), "https://well.test");
    return inputs;
  };

  it("renders the well-known-url field", () => {
    renderForm(<SSOProviderConfigOwnSSOForm save={vi.fn()} configuration={null} />);
    expect(screen.getByText("Well Known URL")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Save" })).toBeInTheDocument();
  });

  it("submits the payload and shows success on a successful save", async () => {
    saveSsoCredential.mockResolvedValue({ isSuccess: true });
    const user = userEvent.setup();
    renderForm(<SSOProviderConfigOwnSSOForm save={vi.fn()} configuration={null} />);

    // The client-secret uses a password input, grab it directly.
    const secret = document.querySelector('input[name="clientSecret"]') as HTMLInputElement;
    await fillOwnSso(user);
    await user.type(secret, "secret");
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(saveSsoCredential).toHaveBeenCalledWith(
        expect.objectContaining({
          provider: "ownsso",
          projectKey: "tenant-1",
          wellKnownUrl: "https://well.test",
          ssoType: 1,
        }),
      ),
    );
    expect(showSuccessToast).toHaveBeenCalled();
  });

  it("shows an error toast when the save is unsuccessful", async () => {
    saveSsoCredential.mockResolvedValue({ isSuccess: false, errors: "nope" });
    const user = userEvent.setup();
    renderForm(<SSOProviderConfigOwnSSOForm save={vi.fn()} configuration={null} />);

    const secret = document.querySelector('input[name="clientSecret"]') as HTMLInputElement;
    await fillOwnSso(user);
    await user.type(secret, "secret");
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(showErrorToast).toHaveBeenCalledWith({ errors: "nope" }));
    expect(showSuccessToast).not.toHaveBeenCalled();
  });
});

describe("SSOProviderConfigBlocksForm", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    oidcExisting.data = undefined;
    useProjectStore.setState({ selectedProject: { tenantId: "tenant-1" } as never });
  });

  it("renders the scope and auto-redirect fields", () => {
    renderForm(<SSOProviderConfigBlocksForm save={vi.fn()} configuration={null} />);
    expect(screen.getByText("Scope")).toBeInTheDocument();
    expect(screen.getByText("Is Auto Redirect")).toBeInTheDocument();
    expect(screen.getByText("True")).toBeInTheDocument();
    expect(screen.getByText("False")).toBeInTheDocument();
  });

  it("maps an existing configuration and submits the OIDC payload", async () => {
    oidcExisting.data = {
      itemId: "oidc-1",
      audience: "https://aud.test",
      clientSecret: "secret",
      redirectUri: "https://cb.test",
      scope: "openid email",
      isAutoRedirect: true,
    };
    saveOidcCredential.mockResolvedValue({ isSuccess: true });
    const user = userEvent.setup();
    renderForm(<SSOProviderConfigBlocksForm save={vi.fn()} configuration={null} />);

    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(saveOidcCredential).toHaveBeenCalledWith(
        expect.objectContaining({
          itemId: "oidc-1",
          projectKey: "tenant-1",
          redirectUri: "https://cb.test",
          isAutoRedirect: true,
          scope: "openid email",
        }),
      ),
    );
    expect(showSuccessToast).toHaveBeenCalled();
  });

  it("shows an error toast when the OIDC save fails", async () => {
    oidcExisting.data = {
      itemId: "oidc-2",
      audience: "https://aud.test",
      clientSecret: "secret",
      redirectUri: "https://cb.test",
      scope: ["openid"],
      isAutoRedirect: false,
    };
    saveOidcCredential.mockResolvedValue({ isSuccess: false, errors: "bad" });
    const user = userEvent.setup();
    renderForm(<SSOProviderConfigBlocksForm save={vi.fn()} configuration={null} />);

    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(showErrorToast).toHaveBeenCalledWith({ errors: "bad" }));
  });
});
