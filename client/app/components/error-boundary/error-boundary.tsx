import { Component, ErrorInfo, ReactNode } from "react";
import { AlertCircle } from "lucide-react";
import { ErrorDisplay } from "@/components/error-display";
import { tStatic } from "@seliseblocks/genesis-os";

interface ErrorBoundaryProps {
  children: ReactNode;
  fallback?: ReactNode;
}

interface ErrorBoundaryState {
  hasError: boolean;
  error: Error | null;
}

export class ErrorBoundary extends Component<
  ErrorBoundaryProps,
  ErrorBoundaryState
> {
  constructor(props: ErrorBoundaryProps) {
    super(props);
    this.state = { hasError: false, error: null };
  }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error("[ErrorBoundary]", error, info.componentStack);
  }

  reset = () => this.setState({ hasError: false, error: null });

  render() {
    if (this.state.hasError) {
      return (
        this.props.fallback ?? (
          <ErrorDisplay
            icon={AlertCircle}
            text={this.state.error?.message ?? tStatic("utilities.shared", "ERROR_BOUNDARY.SOMETHING_WENT_WRONG")}
            className="h-full w-full min-h-[400px]"
          />
        )
      );
    }

    return this.props.children;
  }
}
