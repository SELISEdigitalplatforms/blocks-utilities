import { LogService } from "./log.service";
import { TraceService } from "./trace.service";
import { UsageService } from "./usage.service";

class LmtService {
  constructor(
    public log: LogService,
    public trace: TraceService,
    public usage: UsageService,
  ) {}
}

export const lmtService = new LmtService(new LogService(), new TraceService(), new UsageService());
