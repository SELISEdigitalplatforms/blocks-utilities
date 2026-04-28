import { useParams } from "react-router-dom";
import { AIModelSelectedPage } from "@blocks-ai/pages/aimodel-selected/aimodel-selected";

export default function AiModelSelectedRoute() {
  const { provider } = useParams<{ provider: string }>();
  return <AIModelSelectedPage provider={provider ?? ""} />;
}
