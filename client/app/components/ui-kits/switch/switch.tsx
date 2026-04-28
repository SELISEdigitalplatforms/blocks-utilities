

import * as React from "react";
import * as SwitchPrimitive from "@radix-ui/react-switch";
import { cn } from "@/lib/utils";
import { cva, type VariantProps } from "class-variance-authority";

const switchVariants = cva(
  "relative cursor-pointer rounded-full border-[1px] border-solid transition-colors duration-200",
  {
    variants: {
      size: {
        sm: "h-4 w-8",
        md: "h-6 w-11",
        lg: "h-[24px] w-[44px]",
      },
    },
    defaultVariants: {
      size: "md",
    },
  },
);

const switchThumbVariants = cva(
  "block cursor-pointer rounded-full transition-transform duration-200 will-change-transform",
  {
    variants: {
      size: {
        sm: "size-3 translate-x-[1px] data-[state=checked]:translate-x-[17px]", // 12px thumb
        md: "size-5 translate-x-[1px] data-[state=checked]:translate-x-[23px]", // 20px thumb
        lg: "size-[20px] translate-x-[1px] data-[state=checked]:translate-x-[23px]", // 20px thumb
      },
    },
    defaultVariants: {
      size: "md",
    },
  },
);

export interface SwitchProps
  extends React.ComponentPropsWithoutRef<typeof SwitchPrimitive.Root>,
    VariantProps<typeof switchVariants> {}

const Switch = React.forwardRef<React.ElementRef<typeof SwitchPrimitive.Root>, SwitchProps>(
  ({ className, size, ...props }, ref) => (
    <SwitchPrimitive.Root
      ref={ref}
      className={cn(
        switchVariants({ size }),
        "border-neutral-300 bg-neutral-200 data-[state=checked]:border-blocks-primary-500 data-[state=checked]:bg-blocks-primary-500",
        className,
      )}
      {...props}
    >
      <SwitchPrimitive.Thumb className={cn(switchThumbVariants({ size }), "bg-white shadow-sm")} />
    </SwitchPrimitive.Root>
  ),
);
Switch.displayName = SwitchPrimitive.Root.displayName;

export { Switch };
