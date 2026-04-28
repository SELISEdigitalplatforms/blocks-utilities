import { Link } from "react-router-dom";

export const linkedinStepupDocs = [
  {
    id: "0",
    description: (
      <div>
        <p>
          LinkedIn login allows users to authenticate using their LinkedIn account via OAuth 2.0.
          You’ll need to create a LinkedIn Developer Application, obtain your <b>Client ID</b> and{" "}
          <b>Client Secret</b>, and configure redirect URIs properly in your app.
        </p>
      </div>
    ),
  },
  {
    id: "1",
    description: (
      <div>
        <h4 className="text-lg font-semibold text-high-emphasis">Prerequisites</h4>
        <p>Before starting:</p>
        <ul className="mt-2 list-inside list-disc">
          <li>
            <Link to="https://www.linkedin.com/developers/" className="text-primary">
              Sign in to the LinkedIn Developer Portal
            </Link>
          </li>
          <li>Ensure you have a verified LinkedIn account</li>
        </ul>
      </div>
    ),
  },
  {
    id: "4",
    description: (
      <div>
        <h4 className="text-lg font-semibold text-high-emphasis">Obtain OAuth Credentials</h4>
        <p>
          In your LinkedIn App Dashboard under <b>Auth</b>, you’ll find:
        </p>
        <ul className="mt-2 list-inside list-disc">
          <li><b>Client ID</b></li>
          <li><b>Client Secret</b></li>
        </ul>
        <p className="mt-2">
          Keep these credentials secure and do not expose them on the frontend.
        </p>
      </div>
    ),
  },
];
