import { HttpClient } from "@/lib/http-client";
import { PROJECT_ENDPOINTS } from "@blocks-identifier/constants/endpoint.constant";
import { IGetProjectPayload, IGetProjectResponse, IProjectGroup } from "@/models/project.model";
import { deriveLogicBaseUrl } from "@/lib/blocks-url.util";
import { getRuntimeEnv } from "@/lib/runtime-env";

const logicHttp = new HttpClient(
  deriveLogicBaseUrl(),
  getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
);

export class ProjectService {
  getProjects(page = 0, pageSize = 100, tenantGroupId = ""): Promise<IProjectGroup[]> {
    const url = `${PROJECT_ENDPOINTS.GETS}?page=${page}&pageSize=${pageSize}&tenantGroupId=${tenantGroupId}`;
    return logicHttp.get(url);
  }

  getProject(payload: IGetProjectPayload): Promise<IGetProjectResponse> {
    const url = `${PROJECT_ENDPOINTS.GET}?projectId=${payload.projectId}`;
    return logicHttp.get(url);
  }
}

export const projectService = new ProjectService();