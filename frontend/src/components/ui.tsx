import type { ReactNode } from "react";

export const ErrorText = ({ children }: { children?: string | null }) =>
  children ? (
    <p className="error" role="alert">
      {children}
    </p>
  ) : null;

export const Loading = () => (
  <p className="muted" role="status">
    Caricamento…
  </p>
);

export const Card = ({ children }: { children: ReactNode }) => <section className="card">{children}</section>;
