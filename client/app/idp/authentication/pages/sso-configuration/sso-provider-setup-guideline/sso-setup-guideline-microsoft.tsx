import { Link } from "react-router-dom";

export const microsoftStepupDocs = [
  {
      id: "0",
      description: (
        <div>
          <p>
            The Microsoft social connection allows users to log in to your application using their
            Microsoft account profile.
          </p>
          <p className="mt-2">
            Auth0 automatically syncs user profile data with each login. You can disable syncing if
            needed.
          </p>
        </div>
      ),
    },
    {
      id: "1",
      description: (
        <div>
          <h4 className="text-lg text-high-emphasis">Prerequisites</h4>
          <p>Before you begin:</p>
          <ul className="mt-2 list-inside list-disc">
            <li>
              <Link to="">Sign up for an Azure account</Link>
            </li>
            <li>
              <Link to="">Create an Azure AD Tenant</Link>
            </li>
          </ul>
        </div>
      ),
    },
    {
      id: "2",
      description: (
        <div>
          <h4 className="text-lg text-high-emphasis">Test connection</h4>
          <p>You&apos;re ready to test your connection.</p>
        </div>
      ),
    },
];
