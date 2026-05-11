import { useEffect } from "react";
import { deriveIdpBaseUrl } from "@/lib/blocks-url.util";

export default function ProfilePage() {
  useEffect(() => {
    window.location.href = `${deriveIdpBaseUrl()}/profile`;
  }, []);

  return null;
}
