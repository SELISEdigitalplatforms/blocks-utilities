import { describe, it, expect, vi, beforeEach } from "vitest";
import { render } from "@testing-library/react";
import CallbackPage from "./callback";

let queryState: { isLoading: boolean; isSuccess: boolean };
vi.mock("@tanstack/react-query", () => ({
  useQuery: () => queryState,
}));

const params = new URLSearchParams();
vi.mock("react-router-dom", () => ({
  useSearchParams: () => [params],
}));

vi.mock("@/cross-modules/devops/services/github-info.service", () => ({
  githubInfoService: { verifyAuthorization: vi.fn() },
}));

describe("CallbackPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    queryState = { isLoading: false, isSuccess: false };
    localStorage.clear();
  });

  it("shows a loading spinner while verifying", () => {
    queryState = { isLoading: true, isSuccess: false };
    const { container } = render(<CallbackPage />);
    expect(container.querySelector(".animate-spin")).toBeInTheDocument();
  });

  it("cleans up stored auth data and closes the window on success", () => {
    localStorage.setItem("github_auth_state", "s");
    localStorage.setItem("github_auth_project_key", "pk");
    localStorage.setItem("github_auth_destination", "d");
    const close = vi.fn();
    window.close = close;

    queryState = { isLoading: false, isSuccess: true };
    render(<CallbackPage />);

    expect(localStorage.getItem("isReload")).toBe("true");
    expect(localStorage.getItem("github_auth_state")).toBeNull();
    expect(localStorage.getItem("github_auth_project_key")).toBeNull();
    expect(close).toHaveBeenCalled();
  });

  it("renders nothing when neither loading nor successful", () => {
    const { container } = render(<CallbackPage />);
    expect(container.firstChild).toBeNull();
  });
});
