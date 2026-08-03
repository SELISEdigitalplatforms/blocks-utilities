import { describe, it, expect, vi, beforeEach } from "vitest";
import type { ReactNode } from "react";
import { render, screen } from "@testing-library/react";
import { ProjectDetail } from "./project-detail";

vi.mock("@/lib/domain", () => ({
  getProjectBlocksApiUrl: () => "https://proj.blocks.test",
}));

vi.mock("@/components/copy-to-clipboard-button", () => ({
  CopyToClipboardButton: ({ children }: { children: ReactNode }) => (
    <div>{children}</div>
  ),
}));

const project = {
  name: "My Project",
  tenantId: "tenant-abc-123",
  tenantSlug: "my-project",
  environment: "prod",
  lastUpdatedDate: "2024-01-02T10:30:00Z",
  createdDate: "2024-01-01T10:30:00Z",
} as never;

describe("ProjectDetail", () => {
  beforeEach(() => vi.clearAllMocks());

  it("shows the loading skeleton while loading", () => {
    const { container } = render(
      <ProjectDetail project={undefined} isLoading />,
    );
    expect(container.querySelectorAll(".animate-pulse").length).toBeGreaterThan(
      0,
    );
  });

  it("renders the project fields with a production badge", () => {
    render(<ProjectDetail project={project} isLoading={false} />);
    expect(screen.getByText("My Project")).toBeInTheDocument();
    expect(screen.getByText("X-Blocks-Key")).toBeInTheDocument();
    expect(screen.getByText("my-project")).toBeInTheDocument();
    expect(screen.getByText("Production")).toBeInTheDocument();
    expect(screen.getByText("https://proj.blocks.test")).toBeInTheDocument();
  });

  it("uses the secondary environment label for non-production projects", () => {
    render(
      <ProjectDetail
        project={{ ...project, environment: "dev" } as never}
        isLoading={false}
      />,
    );
    expect(screen.queryByText("Production")).not.toBeInTheDocument();
  });
});
