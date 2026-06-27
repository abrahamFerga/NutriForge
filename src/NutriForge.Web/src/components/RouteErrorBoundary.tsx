import { Component, type ReactNode } from "react";
import { ErrorState } from "@/components/StateMessage";

interface Props {
  children: ReactNode;
}
interface State {
  error: Error | null;
}

/**
 * Catches render and lazy-chunk-load failures in the route subtree so a problem shows a recoverable
 * message instead of a blank screen. Rendered inside the path-keyed wrapper, so it resets on navigation;
 * "Try again" does a full reload, which re-fetches a stale or missing page chunk (the usual cause after a
 * new deploy). Error boundaries must be class components.
 */
export class RouteErrorBoundary extends Component<Props, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  render() {
    if (this.state.error) {
      return (
        <div className="py-10">
          <ErrorState
            error={this.state.error}
            title="This page didn't load"
            onRetry={() => window.location.reload()}
          />
        </div>
      );
    }
    return this.props.children;
  }
}
