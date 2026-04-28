import { useState, useEffect, useRef, ReactNode, useCallback } from "react";

interface InfiniteScrollProps<T> {
  initialData: T[];
  renderItem: (item: T, index: number) => React.ReactNode;
  topFn: (firstItem: T | null) => Promise<T[]>;
  pollingFn: (lastItem: T | null) => Promise<T[]>;
  pollingInterval: number;
  loadingIndicator: ReactNode;
  hasTopMore: boolean;
  bottomIndicator: (cb: () => void) => ReactNode;
}

export const InfiniteScroll = <T,>({
  initialData,
  renderItem,
  topFn,
  pollingFn,
  pollingInterval,
  loadingIndicator,
  bottomIndicator,
  hasTopMore,
}: InfiniteScrollProps<T>) => {
  const [data, setData] = useState(initialData);
  const [isLoading, setIsLoading] = useState(false);
  const [hasMore, setHasMore] = useState(hasTopMore);
  const scrollContainerRef = useRef<HTMLDivElement>(null);
  const [isNewDataAvailable, setNewDataAvailable] = useState(false);

  // Fetch older data when scrolling to the top
  const handleFetchOlderData = useCallback(async () => {
    if (isLoading || !hasMore) return;
    setIsLoading(true);

    try {
      const firstItem = data.length ? data[0] : null;
      const olderData = await topFn(firstItem);
      if (olderData.length > 0) {
        // Save the current scroll height
        const scrollContainer = scrollContainerRef.current;
        const previousScrollHeight = scrollContainer?.scrollHeight || 0;

        // Add older data to the top
        setData((prevData) => [...olderData, ...prevData]);

        // Wait for the DOM to update, then adjust the scroll position
        requestAnimationFrame(() => {
          setTimeout(() => {
            if (scrollContainer) {
              const newScrollHeight = scrollContainer.scrollHeight;
              const heightAdded = newScrollHeight - previousScrollHeight;
              scrollContainer.scrollTop = heightAdded; // Adjust scroll position
            }
          }, 0);
        });
      } else {
        setHasMore(false); // No more older data to fetch
      }
    } catch (error) {
      console.error("Error fetching older data:", error);
    } finally {
      setIsLoading(false);
    }
  }, [data, hasMore, isLoading, topFn]);

  // Fetch newer data periodically
  const handleFetchNewerData = useCallback(async () => {
    try {
      const lastItem = data.length ? data[data.length - 1] : null;
      const newerData = await pollingFn(lastItem);
      if (newerData.length > 0) {
        setNewDataAvailable(true);
        setData((prevData) => [...prevData, ...newerData]);
      }
    } catch (error) {
      console.error("Error fetching newer data:", error);
    }
  }, [data, pollingFn]);

  // Handle scroll to top for fetching older data
  useEffect(() => {
    const scrollContainer = scrollContainerRef.current;

    const handleScroll = () => {
      if (scrollContainer && scrollContainer.scrollTop === 0 && hasMore) {
        handleFetchOlderData();
      }
      if (
        scrollContainer &&
        scrollContainer.scrollTop + scrollContainer.clientHeight >= scrollContainer?.scrollHeight
      ) {
        setNewDataAvailable(false);
      }
    };

    scrollContainer?.addEventListener("scroll", handleScroll);
    return () => scrollContainer?.removeEventListener("scroll", handleScroll);
  }, [handleFetchOlderData, hasMore, isLoading]);

  // Periodically fetch newer data
  useEffect(() => {
    const interval = setInterval(() => {
      handleFetchNewerData();
    }, pollingInterval);

    return () => clearInterval(interval);
  }, [handleFetchNewerData, pollingInterval]);

  const bottomIndicatorHanlder = () => {
    scrollContainerRef.current?.scrollTo({
      top: scrollContainerRef.current?.scrollHeight,
      behavior: "smooth",
    });
    setNewDataAvailable(false);
  };

  useEffect(() => {
    if (scrollContainerRef.current) {
      scrollContainerRef.current?.scrollTo({ top: scrollContainerRef.current.scrollHeight });
    }
  }, []);

  return (
    <div className="relative flex h-full flex-col">
      <div ref={scrollContainerRef} className="h-full overflow-scroll">
        {data.length ? (
          <>
            {isLoading && loadingIndicator}
            {data.map((item, index) => renderItem(item, index))}
          </>
        ) : (
          <div className="flex h-full items-center justify-center text-muted-foreground">
            No logs found
          </div>
        )}
      </div>
      {isNewDataAvailable && bottomIndicator(bottomIndicatorHanlder)}
    </div>
  );
};
