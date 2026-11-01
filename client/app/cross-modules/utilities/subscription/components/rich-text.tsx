import { Fragment } from "react";

const TAG = /<(strong|em|code|a)>(.*?)<\/\1>/g;

/**
 * Renders a translated sentence that carries inline emphasis: `<strong>`, `<em>`, `<code>` and `<a>` in
 * the text become real elements, everything else stays plain text. Built from parsed pieces, never
 * from HTML strings, so a translation cannot inject markup.
 */
export const RichText = ({
  text,
  codeClassName,
  href,
  linkClassName,
}: {
  text: string;
  codeClassName?: string;
  /** Target of an `<a>` in the text. Without one the text is shown plain. */
  href?: string;
  linkClassName?: string;
}) => {
  const pieces: { key: number; node: React.ReactNode }[] = [];
  let cursor = 0;

  for (const match of text.matchAll(TAG)) {
    const start = match.index ?? 0;

    if (start > cursor) {
      pieces.push({ key: cursor, node: text.slice(cursor, start) });
    }

    const [, tag, inner] = match;

    pieces.push({
      key: start,
      node:
        tag === "strong" ? (
          <strong>{inner}</strong>
        ) : tag === "em" ? (
          <em>{inner}</em>
        ) : tag === "a" ? (
          href ? (
            <a href={href} className={linkClassName}>
              {inner}
            </a>
          ) : (
            inner
          )
        ) : (
          <code className={codeClassName}>{inner}</code>
        ),
    });
    cursor = start + match[0].length;
  }

  if (cursor < text.length) {
    pieces.push({ key: cursor, node: text.slice(cursor) });
  }

  return (
    <>
      {pieces.map((piece) => (
        <Fragment key={piece.key}>{piece.node}</Fragment>
      ))}
    </>
  );
};
