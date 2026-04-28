import { Link } from "react-router-dom";

export const githubStepupDocs = [
  {
    id: "0",
    description: (
      <div>
        <p>
          GitHub login allows users to authenticate using their GitHub account. To enable this, you
          need to register your application in the GitHub Developer Settings and obtain OAuth
          credentials (Client ID and Client Secret).
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
            <Link to="https://github.com/login" className="text-primary">
              Sign in to your GitHub account
            </Link>
          </li>
          <li>
            Go to{" "}
            <Link
              to="https://github.com/settings/developers"
              className="text-primary"
            >
              GitHub Developer Settings
            </Link>
          </li>
          <li>Ensure you have administrative access to the organization or repository (if required)</li>
        </ul>
      </div>
    ),
  },
  {
    id: "3",
    description: (
      <div>
        <h4 className="text-lg font-semibold text-high-emphasis">Obtain Client Credentials</h4>
        <p>
          After registering the app, GitHub will generate a <b>Client ID</b> and allow you to view or
          generate a <b>Client Secret</b>.
        </p>
      </div>
    ),
  },
];
