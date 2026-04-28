import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import { useNavigate } from "react-router-dom";
import { getLogFormatTimestamp, getLogLevelClassName } from "@blocks-lmt/utils";
import { ILog } from "../../models/log.model";

export const LogItem = ({ log }: { log: ILog }) => {
  const navigate = useNavigate();

  const onItemClickHandler = (traceId: string) => {
    navigate(`/tracing/timeline/${traceId}`);
  };
  return (
    <div className="flex flex-col">
      <div className="flex flex-col md:flex-row md:items-center">
        <div className="flex items-center gap-2">
          <span className="mr-2 text-high-emphasis">{getLogFormatTimestamp(log.timestamp)}</span>
          <span className={`mr-2 text-sm uppercase ${getLogLevelClassName(log.level)}`}>
            {log.level}
          </span>
        </div>
        <div onClick={() => onItemClickHandler(log.traceId)} className="flex h-6 items-center">
          <CopyToClipboardButton textToCopy={log.traceId} isHoverable>
            <span className="cursor-pointer text-warning-700">[{log.traceId}]</span>
          </CopyToClipboardButton>
        </div>
      </div>
      <div
        className="whitespace-pre-wrap break-words text-left text-sm text-medium-emphasis"
        style={{ width: "calc(80vw - 120px)" }}
      >
        {log.message}
      </div>
    </div>
  );
};
