import React, { Fragment, useCallback, useEffect, useRef, useState } from "react";
import { Bot, X } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { useProjectStore } from "@/store/useProjectStore";
import { useLMTQueryAgentSSE } from "@blocks-ai/hooks/use-agent";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import {
  lmtQueryAgentFormDefaultValue,
  LmtQueryAgentForm,
  lmtQueryAgentSchema,
  parseSSEBuffer,
  generateMessageId,
  getTimestampOrNow,
  handleAgentEvent,
} from "./utils";
import { LMTQueryAgentChatItem } from "./lmt-query-agent-chat-item";
import { LMTQueryAgentChatInput } from "./lmt-query-agent-chat-input";
import { EmptyConversations } from "@blocks-ai/shared/components/chat/empty-conversation/empty-conversation";
import { ChatItemSuggestions } from "@blocks-ai/shared/components/chat/chat-item-suggestions/chat-item-suggestions";

interface ConversationMessage {
  type: "bot" | "human";
  message: string;
  time: string;
  id?: string;
}

interface LMTQueryAgentProps {
  agentName: string;
  onClose?: () => void;
  questions?: string[];
  description?: string;
}

export const LMTQueryAgent: React.FC<LMTQueryAgentProps> = ({
  agentName,
  onClose,
  questions,
  description,
}) => {
  const [session, setSession] = useState<string | null>(null);
  const [isThinking, setIsThinking] = useState<boolean>(false);
  const [conversations, setConversations] = useState<ConversationMessage[]>([]);
  const [suggestions, setSuggestions] = useState<string[]>([]);
  const [currentEvent, setCurrentEvent] = useState<{ message: string } | null>(null);

  const containerRef = useRef<HTMLDivElement>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { streamQuery } = useLMTQueryAgentSSE();

  const form = useForm<LmtQueryAgentForm>({
    defaultValues: lmtQueryAgentFormDefaultValue,
    resolver: zodResolver(lmtQueryAgentSchema),
  });

  const addMessage = useCallback(
    (type: ConversationMessage["type"], message: string, time?: string) => {
      setConversations((prev) => [
        ...prev,
        {
          type,
          message,
          time: getTimestampOrNow(time),
          id: generateMessageId(type),
        },
      ]);
    },
    [],
  );

  const handleStreamEvent = useCallback(
    (eventType: string, eventData: Record<string, unknown>) => {
      if (eventType === "start" && eventData.session_id) setSession(eventData.session_id as string);

      handleAgentEvent(
        { type: eventType, ...eventData },
        {
          showStatus: (status) => setCurrentEvent({ message: status }),
          clearStatus: () => setCurrentEvent(null),
          renderAnswer: (event) => {
            setSuggestions((event.next_step_questions as string[]) || []);
            addMessage("bot", (event.result as string) || "", getTimestampOrNow(event.timestamp));
          },
          showError: (error) => {
            addMessage(
              "bot",
              error || "Something went wrong. Please try again later.",
              getTimestampOrNow(eventData.timestamp),
            );
            setCurrentEvent(null);
            setIsThinking(false);
          },
        },
      );

      if (eventType === "complete") {
        setCurrentEvent(null);
        setIsThinking(false);
      }
    },
    [addMessage],
  );

  const handleSubmit = useCallback(
    async (formData: LmtQueryAgentForm) => {
      if (!formData.query?.trim()) return;
      try {
        addMessage("human", formData.query);
        form.reset();
        setIsThinking(true);
        setCurrentEvent({ message: "Sending..." });
        setSuggestions([]);
        const stream = await streamQuery({
          project_key: tenantId,
          query: formData.query,
          session_id: session,
        });
        const reader = stream.getReader();
        const decoder = new TextDecoder();
        let buffer = "";
        let isDone = false;
        while (!isDone) {
          const { done, value } = await reader.read();
          isDone = done;
          if (done) {
            setIsThinking(false);
            break;
          }
          buffer += decoder.decode(value, { stream: true });
          const { events, remaining } = parseSSEBuffer(buffer);
          buffer = remaining;
          events.forEach(({ eventType, eventData }) => {
            handleStreamEvent(eventType, eventData);
          });
        }
      } catch {
        addMessage("bot", "Something went wrong. Please try again later.");
        setIsThinking(false);
      } finally {
        setTimeout(() => {
          textareaRef.current?.focus();
        }, 0);
      }
    },
    [addMessage, form, handleStreamEvent, session, streamQuery, tenantId],
  );

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
      if (e.key === "Enter" && !e.shiftKey) {
        e.preventDefault();
        form.handleSubmit(handleSubmit)();
      }
    },
    [form, handleSubmit],
  );

  useEffect(() => {
    const container = containerRef.current;
    if (!container) return;
    container.scrollTo({ top: container.scrollHeight, behavior: "smooth" });
  }, [conversations, isThinking]);

  return (
    <div className="flex h-full w-full flex-col items-start">
      <Card className="sticky top-0 z-10 flex w-full shrink-0 flex-row items-center justify-between rounded-none border-b border-border/50 bg-background/80 px-6 py-4 shadow-[0_2px_8px_-2px_rgba(0,0,0,0.1)] backdrop-blur-xl">
        <div className="flex items-center gap-2">
          <div className="rounded-sm bg-blocks-primary-25 px-1 py-0.5">
            <Bot className="aspect-square w-5" aria-hidden="true" />
          </div>
          <h1 className="text-lg font-semibold text-high-emphasis">{agentName}</h1>
        </div>
        {onClose && (
          <Button
            type="button"
            size="icon"
            variant="ghost"
            onClick={onClose}
            className="rounded-full hover:bg-muted/50"
          >
            <X className="h-4 w-4" />
          </Button>
        )}
      </Card>

      <div
        ref={containerRef}
        className="flex min-h-0 w-full flex-1 flex-col gap-8 overflow-y-auto border-l border-r px-6 py-2 pb-1.5 text-base font-normal"
        style={{ scrollbarWidth: "none", msOverflowStyle: "none" } as React.CSSProperties}
      >
        {conversations.length === 0 && (
          <EmptyConversations
            queries={questions}
            onSelect={(question) => handleSubmit({ query: question.trim() })}
            description={description}
          />
        )}
        {conversations.length > 0 &&
          conversations.map((item, index) => (
            <Fragment key={index}>
              <LMTQueryAgentChatItem key={item.id} {...item} />
              {index === conversations.length - 1 &&
                item.type === "bot" &&
                suggestions.length > 0 && (
                  <ChatItemSuggestions
                    suggestions={suggestions}
                    onSelect={(suggestion) => handleSubmit({ query: suggestion })}
                  />
                )}
            </Fragment>
          ))}
        {isThinking && (
          <div className="mb-6 flex flex-col gap-3">
            <p className="text-sm text-medium-emphasis animate-pulse">
              {currentEvent?.message ?? "Thinking…"}
            </p>
          </div>
        )}
      </div>

      <LMTQueryAgentChatInput
        ref={textareaRef}
        form={form}
        isThinking={isThinking}
        onSubmit={handleSubmit}
        onKeyDown={handleKeyDown}
      />
    </div>
  );
};
