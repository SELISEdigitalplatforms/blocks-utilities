/**
 * Pure navigation utilities extracted from oidc-auth-flow.service.ts
 * These handle URL construction and redirects for the OIDC flow.
 */

/**
 * Redirects to the OIDC login page, preserving current query/hash params
 * including brandColor from URL fragments.
 */
export const redirectToLogin = (): void => {
  const currentParams = new URLSearchParams(window.location.search);

  if (window.location.hash) {
    const hash = window.location.hash.substring(1);

    const hashParts = hash.split("&");

    if (/^[A-Fa-f0-9]{6}/.test(hashParts[0])) {
      const colorMatch = hashParts[0].match(/^([A-Fa-f0-9]{6})/)?.[1];
      if (colorMatch && !currentParams.has("brandColor")) {
        currentParams.set("brandColor", `%23${colorMatch}`);
      }

      hashParts.slice(1).forEach((part) => {
        const [key, value] = part.split("=");
        if (key && value && !currentParams.has(key)) {
          currentParams.set(key, value);
        }
      });
    } else {
      const fragmentParams = new URLSearchParams(hash);
      fragmentParams.forEach((value, key) => {
        if (!currentParams.has(key)) {
          currentParams.set(key, value);
        }
      });
    }
  }

  const brandColor = currentParams.get("brandColor");
  if (brandColor && brandColor.startsWith("#") && !brandColor.startsWith("%23")) {
    currentParams.set("brandColor", encodeURIComponent(brandColor));
  }

  const loginUrl = `/oidc/login?${currentParams.toString()}`;
  window.location.href = loginUrl;
};

/**
 * Builds a navigation URL for the given target path, preserving OIDC
 * query/hash parameters and properly encoding brandColor.
 */
export const buildNavigationUrl = (targetPath: string): string => {
  const params = new URLSearchParams(window.location.search);

  if (window.location.hash) {
    const hash = window.location.hash.substring(1);

    const colorMatch = hash.match(/^([A-Fa-f0-9]{6})/);
    if (colorMatch) {
      const colorCode = colorMatch[1];
      const existingBrandColor = params.get("brandColor");
      if (!existingBrandColor || existingBrandColor.trim() === "") {
        params.set("brandColor", `%23${colorCode}`);
      }

      const remainingHash = hash.substring(colorCode.length);
      if (remainingHash.startsWith("&")) {
        const fragmentParams = new URLSearchParams(remainingHash.substring(1));
        fragmentParams.forEach((value, key) => {
          if (!params.has(key)) {
            params.set(key, value);
          }
        });
      }
    } else {
      const fragmentParams = new URLSearchParams(hash);
      fragmentParams.forEach((value, key) => {
        if (!params.has(key)) {
          params.set(key, value);
        }
      });
    }
  }

  const brandColorParam = params.get("brandColor");
  if (!brandColorParam || brandColorParam.trim() === "") {
    const fullUrl = window.location.href;
    const brandColorMatch = fullUrl.match(/brandColor=([^&]*)/)?.[1];
    if (brandColorMatch) {
      const decoded = decodeURIComponent(brandColorMatch);
      if (decoded && decoded !== "") {
        params.set("brandColor", decoded);
      }
    }
  }

  const brandColor = params.get("brandColor");
  if (brandColor) {
    if (brandColor.startsWith("#")) {
      params.set("brandColor", encodeURIComponent(brandColor));
    }
  }

  return `${targetPath}?${params.toString()}`;
};
