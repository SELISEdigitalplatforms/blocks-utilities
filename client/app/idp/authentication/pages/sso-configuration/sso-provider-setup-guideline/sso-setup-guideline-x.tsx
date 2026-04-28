import { Link } from "react-router-dom";

export const xStepupDocs = [
  {
    id: "0",
    description: (
      <div>
        <p>
          Twitter (now X) login enables users to authenticate with their X account using OAuth 2.0.
          To integrate this, you must create a project and app in the{" "}
          <Link to="https://developer.x.com/en/portal/dashboard" className="text-primary">
            X Developer Portal
          </Link>, obtain your <b>Client ID</b> and <b>Client Secret</b>, and configure redirect
          URIs correctly.
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
            Have a verified{" "}
            <Link to="https://twitter.com/" className="text-primary">
              X (Twitter) account
            </Link>
          </li>
          <li>
            Access to the{" "}
            <Link to="https://developer.x.com/en/portal/dashboard" className="text-primary">
              X Developer Portal
            </Link>
          </li>
        </ul>
      </div>
    ),
  },
  {
    id: "4",
    description: (
      <div>
        <h4 className="text-lg font-semibold text-high-emphasis">Obtain Client Credentials</h4>
        <ul className="mt-2 list-inside list-disc">
          <li><b>Client ID</b></li>
          <li><b>Client Secret</b></li>
        </ul>
      </div>
    ),
  }
];
