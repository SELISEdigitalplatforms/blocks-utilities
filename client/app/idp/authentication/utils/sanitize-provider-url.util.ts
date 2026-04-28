/**
 * Strips unresolved C# format-string placeholders (e.g. {0}, {1}) from a
 * provider URL returned by the backend. The BE occasionally returns a URL
 * where the template was only partially evaluated, leaving placeholders like
 * `client_id={0}` while the real values are appended as duplicate params
 * later (e.g. `client_id=com.example.app`). This helper keeps the real
 * values and discards the placeholder entries.
 */
export function sanitizeProviderUrl(url: string): string {
  try {
    const urlObj = new URL(url);
    const allEntries: [string, string][] = [];
    urlObj.searchParams.forEach((value, key) => allEntries.push([key, value]));

    // Build a map of key → last non-placeholder value
    const resolved = new Map<string, string>();
    for (let i = allEntries.length - 1; i >= 0; i--) {
      const [key, value] = allEntries[i];
      if (!/^\{[^}]+\}$/.test(value) && !resolved.has(key)) {
        resolved.set(key, value);
      }
    }

    // Reconstruct params preserving original key order
    const newParams = new URLSearchParams();
    const added = new Set<string>();
    allEntries.forEach(([key]) => {
      if (!added.has(key) && resolved.has(key)) {
        newParams.append(key, resolved.get(key)!);
        added.add(key);
      }
    });

    urlObj.search = newParams.toString();
    return urlObj.toString();
  } catch {
    return url;
  }
}
