const generateRandomState = () => {
  const array = new Uint8Array(32);
  crypto.getRandomValues(array);
  return Array.from(array, (byte) => byte.toString(16).padStart(2, "0")).join("");
};

export const authenticateWithGithub = (extraState?: string, projectKey?: string) => {
  const randomState = generateRandomState();
  
  // Define scopes for personal repository access
  const scopes = ["repo", "user:email", "read:user", "read:repo_hook"].join(" ");

  // Build the OAuth URL with all parameters
  const authUrl = new URL("https://github.com/login/oauth/authorize");
  authUrl.searchParams.set("client_id", import.meta.env.BLOCKS_GITHUB_CLIENT_ID || "");
  authUrl.searchParams.set("scope", scopes);
  authUrl.searchParams.set("state", randomState);

  // Store auth state and project context
  const destination = localStorage.getItem("destination") || "/";
  localStorage.setItem("github_auth_destination", destination);
  localStorage.setItem("github_auth_state", randomState);
  if (projectKey) {
    localStorage.setItem("github_auth_project_key", projectKey);
  }
  
  // Open GitHub OAuth in new tab
  window.open(authUrl.toString(), "_blank", "noopener,noreferrer");
};

// Function to verify state parameter when handling the callback
export const verifyOAuthState = (receivedState: string | null) => {
  const storedState = localStorage.getItem("github_auth_state");
  return storedState === receivedState;
};

export const authenticateWithGitlab = () => {
  console.log("GitLab authentication not yet implemented");
  // Placeholder for GitLab OAuth
};

export const authenticateWithBitbucket = () => {
  console.log("Bitbucket authentication not yet implemented");
  // Placeholder for Bitbucket OAuth
};

export const authenticateWithAzure = () => {
  console.log("Azure DevOps authentication not yet implemented");
  // Placeholder for Azure OAuth
};

export const authenticateWithAws = () => {
  console.log("AWS CodeCommit authentication not yet implemented");
  // Placeholder for AWS authentication
};
