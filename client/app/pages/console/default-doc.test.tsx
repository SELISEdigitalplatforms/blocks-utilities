import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";

import { DefaultDoc } from "./default-doc";

describe("DefaultDoc", () => {
  it("renders the docs, code and cloud cards with links", () => {
    render(<DefaultDoc />);
    expect(screen.getByText("Docs")).toBeInTheDocument();
    expect(screen.getByText("Code")).toBeInTheDocument();
    expect(screen.getByText("Cloud")).toBeInTheDocument();

    const links = screen.getAllByRole("link");
    expect(links).toHaveLength(3);
    expect(links[1]).toHaveAttribute("href", "https://github.com/SELISEdigitalplatforms");
  });
});
