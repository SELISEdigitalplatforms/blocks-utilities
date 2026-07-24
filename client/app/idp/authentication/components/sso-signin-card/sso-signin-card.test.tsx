import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SSOSigninCard } from "./sso-signin-card";

const getSocialLoginEndpoint = vi.fn();
vi.mock("@blocks-idp/authentication/services/oauth.service", () => ({
  oauthService: {
    getSocialLoginEndpoint: (...a: unknown[]) => getSocialLoginEndpoint(...a),
  },
}));

vi.mock("@blocks-idp/authentication/utils/sanitize-provider-url.util", () => ({
  sanitizeProviderUrl: (u: string) => `safe:${u}`,
}));

let theme = "light";
vi.mock("@/hooks/use-theme", () => ({
  useTheme: () => ({ theme }),
}));

const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const provider = {
  provider: "google",
  audience: "https://api.test",
  label: "Google",
  imageSrc: "light.png",
  imageSrcDark: "dark.png",
} as never;

describe("SSOSigninCard", () => {
  let hrefValue = "";
  beforeEach(() => {
    vi.clearAllMocks();
    theme = "light";
    hrefValue = "";
    Object.defineProperty(window, "location", {
      configurable: true,
      value: {
        get href() {
          return hrefValue;
        },
        set href(v: string) {
          hrefValue = v;
        },
      },
    });
  });

  afterEach(() => {
    sessionStorage.clear();
  });

  it("renders the provider logo using the light image", () => {
    render(<SSOSigninCard providerConfig={provider} />);
    expect(screen.getByAltText("google")).toHaveAttribute("src", "light.png");
  });

  it("uses the dark image when the theme is dark", () => {
    theme = "dark";
    render(<SSOSigninCard providerConfig={provider} />);
    expect(screen.getByAltText("google")).toHaveAttribute("src", "dark.png");
  });

  it("shows the label when withLabel is set", () => {
    render(<SSOSigninCard providerConfig={provider} withLabel />);
    expect(screen.getByText(/Sign in with Google/)).toBeInTheDocument();
  });

  it("redirects to the sanitized provider url on success", async () => {
    getSocialLoginEndpoint.mockResolvedValue({ providerUrl: "https://idp.test/auth" });
    const user = userEvent.setup();
    render(<SSOSigninCard providerConfig={provider} />);

    await user.click(screen.getByRole("button"));

    await waitFor(() => expect(hrefValue).toBe("safe:https://idp.test/auth"));
    expect(sessionStorage.getItem("clicked_sso_provider")).toBe("google");
  });

  it("shows an error when the endpoint returns an error", async () => {
    getSocialLoginEndpoint.mockResolvedValue({ error: "bad" });
    const user = userEvent.setup();
    render(<SSOSigninCard providerConfig={provider} />);

    await user.click(screen.getByRole("button"));
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "bad" }),
    );
  });

  it("shows an error when the provider config is incomplete", async () => {
    const user = userEvent.setup();
    render(
      <SSOSigninCard providerConfig={{ ...provider, audience: "" } as never} />,
    );

    await user.click(screen.getByRole("button"));
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({
        errors: "Something went wrong",
      }),
    );
    expect(getSocialLoginEndpoint).not.toHaveBeenCalled();
  });

  it("shows an error when no redirect url is returned", async () => {
    getSocialLoginEndpoint.mockResolvedValue({});
    const user = userEvent.setup();
    render(<SSOSigninCard providerConfig={provider} />);

    await user.click(screen.getByRole("button"));
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({
        errors: "No redirect URL provided.",
      }),
    );
  });
});
