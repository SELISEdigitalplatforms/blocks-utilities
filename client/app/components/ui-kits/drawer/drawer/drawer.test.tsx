import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import {
  Drawer,
  DrawerTrigger,
  DrawerContent,
  DrawerHeader,
  DrawerFooter,
  DrawerTitle,
  DrawerDescription,
  DrawerClose,
} from "./drawer";

describe("Drawer", () => {
  it("renders the trigger and shows content once opened", () => {
    render(
      <Drawer open>
        <DrawerTrigger>Open</DrawerTrigger>
        <DrawerContent>
          <DrawerHeader>
            <DrawerTitle>Title</DrawerTitle>
            <DrawerDescription>Description</DrawerDescription>
          </DrawerHeader>
          <DrawerFooter>
            <DrawerClose>Close</DrawerClose>
          </DrawerFooter>
        </DrawerContent>
      </Drawer>,
    );

    expect(screen.getByText("Title")).toBeInTheDocument();
    expect(screen.getByText("Description")).toBeInTheDocument();
    expect(screen.getByText("Close")).toBeInTheDocument();
  });

  it("passes className overrides through to the header and footer", () => {
    render(
      <Drawer open>
        <DrawerContent>
          <DrawerHeader className="custom-header">
            <DrawerTitle>T</DrawerTitle>
          </DrawerHeader>
          <DrawerFooter className="custom-footer">footer</DrawerFooter>
        </DrawerContent>
      </Drawer>,
    );

    expect(document.querySelector(".custom-header")).toBeInTheDocument();
    expect(document.querySelector(".custom-footer")).toBeInTheDocument();
  });
});
