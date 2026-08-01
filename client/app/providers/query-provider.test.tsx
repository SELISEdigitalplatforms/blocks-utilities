import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { useQuery } from "@tanstack/react-query";

import QueryProvider, { getQueryClient } from "./query-provider";

const Consumer = () => {
  const { data } = useQuery({ queryKey: ["probe"], queryFn: () => "ready" });
  return <div>child sees: {data ?? "loading"}</div>;
};

describe("QueryProvider", () => {
  it("provides a shared query client to its children", async () => {
    render(
      <QueryProvider>
        <Consumer />
      </QueryProvider>,
    );
    expect(await screen.findByText("child sees: ready")).toBeInTheDocument();
  });

  it("returns the same singleton query client across calls", () => {
    expect(getQueryClient()).toBe(getQueryClient());
  });
});
