interface OIDCParams {
  projectKey?: string;
  userName?: string;
  logoUrl?: string;
  themeColor: string;
  clientId?: string;
  state?: string;
  nonce?: string;
  scope?: string;
  redirectUri?: string;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [key: string]: string | undefined;
}

/**
 * Decodes a value that may have been encoded multiple times
 */
const fullyDecodeURIComponent = (value: string): string => {
  let decoded = value;
  let previous = "";
  
  while (decoded !== previous && decoded.includes("%")) {
    previous = decoded;
    try {
      decoded = decodeURIComponent(decoded);
    } catch {
      break; 
    }
  }
  
  return decoded;
};

/**
 * Normalizes a color value to #XXXXXX format
 */
const normalizeColorValue = (value: string | null | undefined): string => {
  if (!value || value === "" || value === "&") {
    return "#124091";
  }

  let color = fullyDecodeURIComponent(value);
  
  color = color.replace(/&.*$/, "");
  
  if (/^[A-Fa-f0-9]{6}$/.test(color)) {
    return `#${color}`;
  }
  
  if (color.startsWith("#") && /^#[A-Fa-f0-9]{6}$/.test(color)) {
    return color;
  }
  
  return "#124091";
};

/**
 * Extracts OIDC parameters from URL, handling both query string and hash
 * This handles cases where # in brandColor causes it to be split into hash
 */
export const extractOIDCParams = (debug = false): OIDCParams => {
  if (typeof window === "undefined") {
    return { themeColor: "#124091" };
  }

  const searchParams = new URLSearchParams(window.location.search);
  const hash = window.location.hash;
  const fullUrl = window.location.href;

  let projectKey = searchParams.get("x-blocks-key") || undefined;
  let userName = searchParams.get("userName") || undefined;
  let clientId = searchParams.get("clientId") || undefined;
  let logoUrl = searchParams.get("logoUrl") || undefined;
  let themeColor = searchParams.get("brandColor") || undefined;
  let state = searchParams.get("state") || undefined;
  let nonce = searchParams.get("nonce") || undefined;
  let scope = searchParams.get("scope") || undefined;
  let redirectUri = searchParams.get("redirect_uri") || undefined;


  if (!themeColor || themeColor === "" || themeColor === "&") {
    const brandColorMatch = fullUrl.match(/[?&]brandColor=([^&#]*)/)?.[1];
    if (brandColorMatch && brandColorMatch !== "" && brandColorMatch !== "&") {
      themeColor = brandColorMatch;
    }
  }
    
  if (hash) {
    const hashContent = hash.substring(1);
    
    const colorMatch = hashContent.match(/^([A-Fa-f0-9]{6})/)?.[1];
    if (colorMatch) {
      if (!themeColor || themeColor === "" || themeColor === "&") {
        themeColor = `#${colorMatch}`;
      }
      
      const remainingHash = hashContent.substring(6);
      
      if (remainingHash.startsWith("&")) {
        const hashParams = new URLSearchParams(remainingHash.substring(1));
        
        if (!logoUrl) {
          logoUrl = hashParams.get("logoUrl") || undefined;
        }
        if (!projectKey) {
          projectKey = hashParams.get("x-blocks-key") || undefined;
        }
        if (!clientId) {
          clientId = hashParams.get("clientId") || undefined;
        }
        if (!userName) {
          userName = hashParams.get("userName") || undefined;
        }
        if (!state) {
          state = hashParams.get("state") || undefined;
        }
        if (!nonce) {
          nonce = hashParams.get("nonce") || undefined;
        }
        if (!scope) {
          scope = hashParams.get("scope") || undefined;
        }
        if (!redirectUri) {
          redirectUri = hashParams.get("redirect_uri") || undefined;
        }
      } else {
        try {
          const hashParams = new URLSearchParams(hashContent);
          
          if (!themeColor && hashParams.has("brandColor")) {
            themeColor = hashParams.get("brandColor") || undefined;
          }
          if (!logoUrl && hashParams.has("logoUrl")) {
            logoUrl = hashParams.get("logoUrl") || undefined;
          }
          if (!projectKey && hashParams.has("x-blocks-key")) {
            projectKey = hashParams.get("x-blocks-key") || undefined;
          }
          if (!clientId && hashParams.has("clientId")) {
            clientId = hashParams.get("clientId") || undefined;
          }
          if (!userName && hashParams.has("userName")) {
            userName = hashParams.get("userName") || undefined;
          }
          if (!state && hashParams.has("state")) {
            state = hashParams.get("state") || undefined;
          }
          if (!nonce && hashParams.has("nonce")) {
            nonce = hashParams.get("nonce") || undefined;
          }
          if (!scope && hashParams.has("scope")) {
            scope = hashParams.get("scope") || undefined;
          }
          if (!redirectUri && hashParams.has("redirect_uri")) {
            redirectUri = hashParams.get("redirect_uri") || undefined;
          }
        } catch (e) {
          if (debug) console.error("Failed to parse hash as params:", e);
        }
      }
    }
  }

  if (!logoUrl) {
    const logoUrlMatch = fullUrl.match(/[&#]logoUrl=([^&#]*)/)?.[1];
    if (logoUrlMatch) {
      logoUrl = fullyDecodeURIComponent(logoUrlMatch);
    }
  } else {
    logoUrl = fullyDecodeURIComponent(logoUrl);
  }

  const normalizedThemeColor = normalizeColorValue(themeColor);

  const result = {
    projectKey,
    userName,
    logoUrl,
    themeColor: normalizedThemeColor,
    clientId,
    state,
    nonce,
    scope,
    redirectUri,
  };

  return result;
};

/**
 * Builds a navigation URL with current OIDC params preserved
 * IMPORTANT: Only encodes values ONCE, even if called multiple times
 */
export const buildOIDCNavigationUrl = (path: string): string => {
  const params = extractOIDCParams(); 
  const searchParams = new URLSearchParams();

  if (params.projectKey) searchParams.set("x-blocks-key", params.projectKey);
  if (params.userName) searchParams.set("userName", params.userName);
  if (params.clientId) searchParams.set("clientId", params.clientId);
  if (params.logoUrl) searchParams.set("logoUrl", params.logoUrl);
  

  if (params.themeColor) {
    searchParams.set("brandColor", params.themeColor);
  }
  if (params.state) searchParams.set("state", params.state);
  if (params.nonce) searchParams.set("nonce", params.nonce);
  if (params.scope) searchParams.set("scope", params.scope);
  if (params.redirectUri) searchParams.set("redirect_uri", params.redirectUri);

  const queryString = searchParams.toString();
  return queryString ? `${path}?${queryString}` : path;
};

/**
 * Gets current params as URLSearchParams for redirects
 */
export const getCurrentOIDCParams = (): URLSearchParams => {
  const params = extractOIDCParams(); 
  const searchParams = new URLSearchParams();

  if (params.projectKey) searchParams.set("x-blocks-key", params.projectKey);
  if (params.userName) searchParams.set("userName", params.userName);
  if (params.clientId) searchParams.set("clientId", params.clientId);
  if (params.logoUrl) searchParams.set("logoUrl", params.logoUrl);
  if(params.state) searchParams.set("state", params.state);
  if(params.nonce) searchParams.set("nonce", params.nonce);
  if(params.scope) searchParams.set("scope", params.scope);
  if(params.redirectUri) searchParams.set("redirect_uri", params.redirectUri);

  if (params.themeColor) {
    searchParams.set("brandColor", params.themeColor);
  }

  return searchParams;
};