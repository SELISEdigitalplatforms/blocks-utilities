

import React, { forwardRef, useEffect, useImperativeHandle, useMemo, useRef, useState } from "react";
import BeefreeSDK from "@beefree.io/sdk";
// import Bee from "@mailupinc/bee-plugin";
// import {
//   IBeeConfig,
//   //   IMergeContent,
//   IMergeTag,
//   ISpecialLink,
// } from "@mailupinc/bee-plugin/dist/types/bee";
import { blankTemplate } from "@blocks-utilities/mail/constants/email-template";
import { emailService } from "@blocks-utilities/mail/services/email.services";
import {
  IBeeConfig,
  IMergeTag,
  ISpecialLink,
  IEntityContentJson,
  IToken,
} from "@beefree.io/sdk/dist/types/bee";
import Bee from "@beefree.io/sdk";
// const BEE_TEMPLATE_URL = "https://rsrc.getbee.io/api/templates/m-bee";
const BEEJS_URL = "https://app-rsrc.getbee.io/plugin/BeePlugin.js";
const API_AUTH_URL = "https://auth.getbee.io/loginV2";
// Must match the `PluginProvider` value stored in the TemplatePluginConfigs
// collection for the backend to find the right credentials/config.
const BEE_PLUGIN_PROVIDER = "Bee";
const BEE_PLUGIN_UID = "prod-874A6A2B-F0F5-43EB-A458-47C2C1A21231-d7d199f0-afab-44aa-9fc8-906cfb184730";

const BEE_PLUGIN_CONTAINER_ID = "bee-plugin-container";

const specialLinks: ISpecialLink[] = [
  {
    type: "unsubscribe",
    label: "SpecialLink.Unsubscribe",
    link: "http://[unsubscribe]/",
  },
  {
    type: "subscribe",
    label: "SpecialLink.Subscribe",
    link: "http://[subscribe]/",
  },
];
const mergeTags: IMergeTag[] = [
  {
    name: "tag 1",
    value: "[tag1]",
  },
  {
    name: "tag 2",
    value: "[tag2]",
  },
];

interface IBeePluginStarterProps {
  onBeeSave(data: { htmlFile: string; jsonFile: string }): void;
  onBeeTemplateLoad?: (isLoaded: boolean) => void;
  jsonFile?: IEntityContentJson | Record<string, unknown>;
}

const BeePluginStarter = forwardRef(function Inner(
  { onBeeSave, onBeeTemplateLoad, jsonFile = blankTemplate }: IBeePluginStarterProps,
  ref,
) {
  const [bee, setBee] = useState<Bee | null>(null);

  // Use refs for callbacks to avoid stale closures without triggering effect re-runs
  const onBeeSaveRef = useRef(onBeeSave);
  const onBeeTemplateLoadRef = useRef(onBeeTemplateLoad);

  useEffect(() => {
    onBeeSaveRef.current = onBeeSave;
  }, [onBeeSave]);
  useEffect(() => {
    onBeeTemplateLoadRef.current = onBeeTemplateLoad;
  }, [onBeeTemplateLoad]);

  // Track whether the SDK has been initialized to prevent repeated initialization
  const isInitializedRef = useRef(false);

  const beeConfig: IBeeConfig = useMemo(
    () => ({
      uid: BEE_PLUGIN_UID,
      container: BEE_PLUGIN_CONTAINER_ID,
      autosave: 30,
      language: "en-US",
      specialLinks,
      mergeTags,
      onSave: (jsonFile, htmlFile) => {
        // Use ref to always get the latest callback without re-creating beeConfig
        onBeeSaveRef.current({ jsonFile, htmlFile });
      },
      onLoad: () => {
        // console.warn("*** [integration] loading a new template...");
        onBeeTemplateLoadRef.current?.(true);
      },
      onAutoSave: (jsonFile) => {
        // console.log(`${new Date().toISOString()} autosaving...,`, jsonFile);
      },
      onSend: (htmlFile) => console.log("onSend"),
      onError: (errorMessage) => console.log("onError ", errorMessage),
      onChange: (msg, response) =>
        console.warn("*** [integration] (OnChange) message --> ", msg, response),
      onWarning: (e) => console.warn("*** [integration] (OnWarning) message --> ", e.message),
      onPreview: () => console.warn("*** [integration] --> (onPreview) "),
    }),
    // Empty deps: beeConfig is created once and callbacks are accessed via refs
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [],
  );

  useEffect(() => {
    // Guard: Prevent SDK from being initialized multiple times
    if (isInitializedRef.current) {
      return;
    }
    isInitializedRef.current = true;

    emailService
      .fetchTemplatePluginToken(BEE_PLUGIN_PROVIDER, BEE_PLUGIN_UID)
      .then((response) => {
        if (!response?.access_token) {
          throw new Error("No access token returned for the template plugin.");
        }
        const token: IToken = { access_token: response.access_token, v2: true };
        return new BeefreeSDK(token, { authUrl: API_AUTH_URL, beePluginUrl: BEEJS_URL });
      })
      .then((beeInstance) => {
        if (!beeInstance) return null;
        return beeInstance.start(beeConfig, jsonFile ?? blankTemplate);
      })
      .then((instance) => {
        // console.log("*** [integration] --> (start) ", instance);
        setBee(instance as Bee);
      })
      .catch((error) => {
        // Reset initialization flag so retry is possible on next mount
        isInitializedRef.current = false;
        // Only log non-CORS errors to avoid console flooding
        if (error?.message?.includes("CORS") || error?.name === "TypeError") {
          console.warn("BeePlugin initialization failed due to CORS/network issue. Please check your network connection.");
        } else {
          console.error("error during iniziatialization --> ", error);
        }
      });
  }, [beeConfig, jsonFile]);

  useImperativeHandle(ref, () => {
    return {
      submit() {
        // console.log("submit");
        bee?.save();
      },
      preview() {
        // console.log("preview");
        bee?.preview();
      },
      reset() {
        // console.log("reset");
        bee?.load(jsonFile as IEntityContentJson);
      },
    };
  }, [bee, jsonFile]);

  return (
    <>
      <div id={BEE_PLUGIN_CONTAINER_ID} className="h-[calc(100vh-60px)] w-full" />
      {/* {isBeeStarted && (
        <div id={BEE_PLUGIN_CONTAINER_ID} className="h-[calc(100vh-60px)] w-full" />
      )} */}
    </>
  );
});

export default BeePluginStarter;
