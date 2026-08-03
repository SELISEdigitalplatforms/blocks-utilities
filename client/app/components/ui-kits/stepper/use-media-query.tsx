import * as React from "react";

export function useMediaQuery(query: string) {
	const subscribe = React.useCallback(
		(callback: () => void) => {
			const result = matchMedia(query);
			result.addEventListener("change", callback);
			return () => result.removeEventListener("change", callback);
		},
		[query],
	);

	const getSnapshot = () => matchMedia(query).matches;

	return React.useSyncExternalStore(subscribe, getSnapshot, () => false);
}
