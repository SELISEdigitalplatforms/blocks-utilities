import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";

import { PeopleManagement } from "./people-management";

const h = vi.hoisted(() => ({ result: { isLoading: false, data: undefined as unknown } }));
vi.mock("@blocks-identifier/hooks/use-people", () => ({
  useGetPeople: () => h.result,
}));

describe("PeopleManagement", () => {
  beforeEach(() => {
    h.result = { isLoading: false, data: undefined };
  });

  it("shows loading skeletons while fetching", () => {
    h.result = { isLoading: true, data: undefined };
    const { container } = render(<PeopleManagement />);
    expect(container.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
  });

  it("shows the empty state when there are no people", () => {
    h.result = { isLoading: false, data: { peoples: [], isOwner: true } };
    render(<PeopleManagement />);
    expect(screen.getByText("No people found in this project.")).toBeInTheDocument();
  });

  it("renders people rows with names, emails and environment badges", () => {
    h.result = {
      isLoading: false,
      data: {
        peoples: [
          {
            peopleDetails: { firstName: "Ada", lastName: "Lovelace", email: "ada@calc.dev" },
            sharedEnviroments: [{ tenantId: "t1", enviroment: "dev" }],
          },
          {
            peopleDetails: { email: "grace@navy.dev" },
            sharedEnviroments: [],
          },
        ],
        isOwner: false,
      },
    };
    render(<PeopleManagement />);
    expect(screen.getByText("Ada Lovelace")).toBeInTheDocument();
    expect(screen.getByText("ada@calc.dev")).toBeInTheDocument();
    expect(screen.getByText("dev")).toBeInTheDocument();
    // The second person has no name, so the email is shown as the primary label.
    expect(screen.getAllByText("grace@navy.dev").length).toBeGreaterThan(0);
  });
});
