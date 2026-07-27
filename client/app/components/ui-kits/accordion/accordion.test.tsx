import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import {
  Accordion,
  AccordionItem,
  AccordionTrigger,
  AccordionContent,
} from "./accordion";

describe("Accordion", () => {
  it("expands the content when the trigger is clicked", async () => {
    const user = userEvent.setup();
    render(
      <Accordion type="single" collapsible>
        <AccordionItem value="one">
          <AccordionTrigger>Section one</AccordionTrigger>
          <AccordionContent>Panel one body</AccordionContent>
        </AccordionItem>
      </Accordion>,
    );

    const trigger = screen.getByRole("button", { name: "Section one" });
    expect(trigger).toHaveAttribute("data-state", "closed");
    await user.click(trigger);
    expect(trigger).toHaveAttribute("data-state", "open");
    expect(screen.getByText("Panel one body")).toBeInTheDocument();
  });
});
