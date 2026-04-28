import { Link } from "react-router-dom";

export const googleStepupDocs = [
  {
    id: "0",
    description: (
      <div>
        <p>
          Google login allows users to authenticate using their Google account. You need to create
          OAuth credentials and configure your app in the Google Cloud Console.
        </p>
      </div>
    ),
  },
  {
    id: "1",
    description: (
      <div>
        <h4 className="text-lg font-semibold text-high-emphasis">Prerequisites</h4>
        <p>Before you begin:</p>
        <ul className="mt-2 list-inside list-disc">
          <li>
            <Link to="https://console.cloud.google.com/" className="text-primary">
              Sign in to Google Cloud Console
            </Link>
          </li>
          <li>Create a new project or select an existing one</li>
        </ul>
      </div>
    ),
  },
  {
    id: "2",
    description: (
      <div>
        <h4 className="text-lg font-semibold text-high-emphasis">Configure Consent Screen</h4>
        <p>In Google Cloud Console:</p>
        <ul className="mt-2 list-inside list-disc">
          <li>
            Go to{" "}
            <Link
              to="https://console.cloud.google.com/apis/credentials/consent"
              className="text-primary"
            >
              OAuth consent screen
            </Link>
          </li>
          <li>Select “External” as user type</li>
          <li>Fill required fields like app name, support email, and scopes</li>
          <li>Save and continue through the steps</li>
        </ul>
      </div>
    ),
  },
  {
    id: "3",
    description: (
      <div>
        <h4 className="text-lg font-semibold text-high-emphasis">Create OAuth 2.0 Credentials</h4>
        <ul className="mt-2 list-inside list-disc">
          <li>
            Go to{" "}
            <Link to="https://console.cloud.google.com/apis/credentials" className="text-primary">
              APIs & Services &gt; Credentials
            </Link>
          </li>
          <li>Click “Create Credentials” &gt; “OAuth Client ID”</li>
          <li>Select “Web application” as application type</li>
          <li>
            Add your domain in <b>Authorized JavaScript origins</b>:<br />
            <code>https://yourdomain.com</code>
          </li>
          <li>
            Add redirect URI:
            <br />
            <code>https://yourdomain.com/login/callback</code>
          </li>
          <li>Click “Create” and note the Client ID and Client Secret</li>
        </ul>
      </div>
    ),
  },
  {
    id: "4",
    description: (
      <div>
        <h4 className="text-lg font-semibold text-high-emphasis">Configure in Admin Panel</h4>
        <p>
          Copy the following values from the Google Cloud Console and enter them into your admin
          panel:
        </p>
        <ul className="mt-2 list-inside list-disc">
          <li>
            <b>Client ID</b>
          </li>
          <li>
            <b>Client Secret</b>
          </li>
          <li>
            <b>Redirect URI</b>: <code>https://yourdomain.com/login/callback</code>
          </li>
        </ul>
      </div>
    ),
  },
  {
    id: "5",
    description: (
      <div>
        <h4 className="text-lg font-semibold text-high-emphasis">Test Connection</h4>
        <p>Log out and try logging in via Google to verify the integration.</p>
      </div>
    ),
  },
];
