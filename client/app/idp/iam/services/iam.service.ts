import { OrganizationService } from "./organization.service";
import { PermissionService } from "./permission.service";

class IAMService {
  constructor(
    public permission: PermissionService,
    public organization: OrganizationService,
  ) {}
}

export const iamService = new IAMService(new PermissionService(), new OrganizationService());
