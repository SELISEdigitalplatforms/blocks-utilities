import { AuthConfiguration } from "./auth-config.service";

class Authentication {
  constructor(public configuration: AuthConfiguration) {}
}

export const authenticationService = new Authentication(new AuthConfiguration());
