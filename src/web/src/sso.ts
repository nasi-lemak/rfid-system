import { UserManager, WebStorageStateStore } from 'oidc-client-ts';
import { get } from './api/client';

export interface AuthConfig { passwordLogin: boolean; sso?: { authority: string; clientId: string; scopes: string; audience?: string | null } | null }

let cached: Promise<AuthConfig> | null = null;
export const authConfig = () => (cached ??= get<AuthConfig>('/api/auth/config').catch(() => ({ passwordLogin: true, sso: null } as AuthConfig)));

export function userManager(cfg: NonNullable<AuthConfig['sso']>) {
  return new UserManager({
    authority: cfg.authority,
    client_id: cfg.clientId,
    redirect_uri: `${window.location.origin}/auth/callback`,
    post_logout_redirect_uri: window.location.origin,
    response_type: 'code',
    scope: cfg.scopes || 'openid profile email',
    userStore: new WebStorageStateStore({ store: window.sessionStorage }),
    automaticSilentRenew: false,
  });
}

/** The token the API should see: an access token when an API audience is configured, otherwise the ID token (audience = client id). */
export const apiTokenFrom = (u: { access_token?: string; id_token?: string }, cfg: NonNullable<AuthConfig['sso']>) => (cfg.audience ? u.access_token : u.id_token) ?? u.access_token ?? '';
