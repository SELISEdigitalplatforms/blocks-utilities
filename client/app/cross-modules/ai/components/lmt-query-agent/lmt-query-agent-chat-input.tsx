import React, { useRef, useEffect, forwardRef } from "react";
import { ArrowUp } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { UseFormReturn } from "react-hook-form";
import { LmtQueryAgentForm } from "./utils";

interface LMTQueryAgentChatInputProps {
  form: UseFormReturn<LmtQueryAgentForm>;
  isThinking: boolean;
  onSubmit: (data: LmtQueryAgentForm) => void;
  onKeyDown?: (e: React.KeyboardEvent<HTMLTextAreaElement>) => void;
}

export const LMTQueryAgentChatInput = forwardRef<HTMLTextAreaElement, LMTQueryAgentChatInputProps>(
  ({ form, isThinking, onSubmit, onKeyDown }, _ref) => {
    const textareaRef = useRef<HTMLTextAreaElement>(null);
    const currentQuery = form.watch("query");
    const isSubmitDisabled = isThinking || !currentQuery?.trim();

    useEffect(() => {
      const textarea = textareaRef.current;
      if (!textarea) return;
      textarea.style.height = "auto";
      textarea.style.height = `${textarea.scrollHeight}px`;
    }, [currentQuery]);

    return (
      <div className="w-full shrink-0 px-6 pb-4">
        <Form {...form}>
          <form
            className="w-full"
            onSubmit={form.handleSubmit(onSubmit)}
            role="form"
            aria-label="Send message form"
          >
            <div className="flex items-center gap-1 rounded-sm border p-2.5">
              <FormField
                control={form.control}
                name="query"
                render={({ field }) => (
                  <FormItem className="flex-1">
                    <FormControl>
                      <Textarea
                        {...field}
                        ref={textareaRef}
                        rows={1}
                        placeholder="Type a message..."
                        className="max-h-[120px] min-h-fit w-full resize-none border-0 p-0 text-base text-medium-emphasis focus-visible:ring-0 focus-visible:ring-offset-0"
                        style={{ scrollbarWidth: "none", msOverflowStyle: "none" } as React.CSSProperties}
                        onKeyDown={onKeyDown}
                        disabled={isThinking}
                        aria-label="Message input"
                      />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <Button
                type="submit"
                size="sm"
                variant="ghost"
                className="h-8 w-8 rounded-sm bg-secondary p-1.5"
                disabled={isSubmitDisabled}
                aria-label="Send message"
              >
                <ArrowUp className="aspect-square w-4" aria-hidden="true" />
              </Button>
            </div>
          </form>
        </Form>
      </div>
    );
  },
);

LMTQueryAgentChatInput.displayName = "LMTQueryAgentChatInput";
