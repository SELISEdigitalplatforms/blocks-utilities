import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { useProjectStore } from "@seliseblocks/genesis-os";
import ProviderButtons from "./render-provider";

const navigate = vi.fn();
vi.mock("react-router", async () => {
  const actual =
    await vi.importActual<typeof import("react-router")>("react-router");
  return { ...actual, useNavigate: () => navigate };
});

const authenticateWithGithub = vi.fn();
vi.mock("@/cross-modules/devops/services/providers.service", () => ({
  authenticateWithGithub: (...a: unknown[]) => authenticateWithGithub(...a),
  authenticateWithGitlab: vi.fn(),
  authenticateWithBitbucket: vi.fn(),
  authenticateWithAzure: vi.fn(),
  authenticateWithAws: vi.fn(),
}));

let verifyAuth: { data: { isSuccess: boolean } | undefined } = { data: undefined };
vi.mock("@/cross-modules/devops/hooks/github-info", () => ({
  useValidateAuthorization: () => verifyAuth,
}));

const renderButtons = (props = {}) =>
  render(
    <MemoryRouter>
      <ProviderButtons destination="/devops/configure" {...props} />
    </MemoryRouter>,
  );

describe("ProviderButtons", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
    verifyAuth = { data: undefined };
    useProjectStore.setState({ selectedProject: { tenantId: "pk1" } });
  });

  it("renders a button per provider and stores the destination", () => {
    renderButtons();
    expect(
      screen.getByRole("button", { name: /Continue with GitHub/ }),
    ).toBeEnabled();
    expect(
      screen.getByRole("button", { name: /Continue with GitLab/ }),
    ).toBeDisabled();
    expect(localStorage.getItem("destination")).toBe("/devops/configure");
  });

  it("starts github auth when not yet authorized", () => {
    renderButtons();
    fireEvent.click(screen.getByRole("button", { name: /Continue with GitHub/ }));
    expect(authenticateWithGithub).toHaveBeenCalledWith("", "pk1");
  });

  it("calls onClose when github is already authorized", () => {
    verifyAuth = { data: { isSuccess: true } };
    const onClose = vi.fn();
    renderButtons({ onClose });
    fireEvent.click(screen.getByRole("button", { name: /Continue with GitHub/ }));
    expect(onClose).toHaveBeenCalledWith(true);
    expect(authenticateWithGithub).not.toHaveBeenCalled();
  });

  it("navigates to the destination when authorized and no onClose is given", () => {
    verifyAuth = { data: { isSuccess: true } };
    renderButtons();
    fireEvent.click(screen.getByRole("button", { name: /Continue with GitHub/ }));
    expect(navigate).toHaveBeenCalledWith("/devops/configure");
  });
});
