import { UserManager, WebStorageStateStore } from "oidc-client-ts";
import type { User } from "oidc-client-ts";

const authority = import.meta.env.VITE_OIDC_AUTHORITY as string | undefined;
const clientId = import.meta.env.VITE_OIDC_CLIENT_ID as string | undefined;
const redirectUri = import.meta.env.VITE_OIDC_REDIRECT_URI as string | undefined;
const scope = import.meta.env.VITE_OIDC_SCOPE as string | undefined;

export const authConfigured = Boolean(authority && clientId && redirectUri);
export const userManager = authConfigured
  ? new UserManager({
      authority,
      client_id: clientId,
      redirect_uri: redirectUri,
      response_type: "code",
      scope: scope || "openid profile",
      userStore: new WebStorageStateStore({ store: window.sessionStorage }),
      automaticSilentRenew: true,
    })
  : undefined;

export function getDisplayName(user: User | null): string {
  const name = user?.profile.name ?? user?.profile.preferred_username;
  return typeof name === "string" ? name : "";
}
