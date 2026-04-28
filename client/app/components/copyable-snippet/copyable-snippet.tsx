import { Check, Copy } from "lucide-react";
import { useState } from "react";
import { Prism as SyntaxHighlighter } from "react-syntax-highlighter";

const customStyle = {
  'code[class*="language-"]': {
    color: "hsl(var(--foreground))",
    background: "hsl(var(--surface-app))",
    fontFamily: 'Consolas, Monaco, "Andale Mono", "Ubuntu Mono", monospace',
    fontSize: "0.875rem",
    textAlign: "left" as const,
    whiteSpace: "pre" as const,
    wordSpacing: "normal",
    wordBreak: "normal" as const,
    wordWrap: "normal" as const,
    lineHeight: "1.5",
    tabSize: 4,
    hyphens: "none" as const,
  },
  'pre[class*="language-"]': {
    color: "hsl(var(--foreground))",
    background: "hsl(var(--surface-app))",
    fontFamily: 'Consolas, Monaco, "Andale Mono", "Ubuntu Mono", monospace',
    fontSize: "0.875rem",
    textAlign: "left" as const,
    whiteSpace: "pre" as const,
    wordSpacing: "normal",
    wordBreak: "normal" as const,
    wordWrap: "normal" as const,
    lineHeight: "1.5",
    tabSize: 4,
    hyphens: "none" as const,
    padding: "1rem",
    margin: "0.5rem 0",
    overflow: "auto" as const,
    borderRadius: "0.375rem",
  },
};

interface CopyableSnippetProps {
  code: string;
  language?: string;
  isCopyable: boolean;
}

export const CopyableSnippet = ({
  code,
  language = "bash",
  isCopyable = true,
}: CopyableSnippetProps) => {
  const [copied, setCopied] = useState(false);

  const handleCopy = async () => {
    try {
      if (navigator?.clipboard?.writeText) {
        await navigator.clipboard.writeText(code);
      } else {
        const textArea = document.createElement("textarea");
        textArea.value = code;
        textArea.style.position = "fixed";
        textArea.style.left = "-999999px";
        textArea.style.top = "-999999px";
        document.body.appendChild(textArea);
        textArea.focus();
        textArea.select();
        document.execCommand("copy");
        textArea.remove();
      }
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch (error) {
      console.error("Failed to copy text:", error);
    }
  };

  return (
    <div className="relative">
      {isCopyable && (
        <button
          onClick={handleCopy}
          className="absolute -top-[28px] right-2 z-10 text-muted-foreground transition hover:text-foreground"
          aria-label="Copy code"
        >
          {copied ? <Check className="h-4 w-4 text-green-500" /> : <Copy className="h-4 w-4" />}
        </button>
      )}
      <SyntaxHighlighter language={language} style={customStyle}>
        {code.trim()}
      </SyntaxHighlighter>
    </div>
  );
};
