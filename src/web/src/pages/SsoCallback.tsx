import { useEffect, useState } from 'react';
import { useAuth } from '../auth';
import { apiTokenFrom, authConfig, userManager } from '../sso';

/** /auth/callback – completes the OIDC authorization-code flow and exchanges the external token for a platform session. */
export default function SsoCallback() {
  const { loginWithToken } = useAuth();
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const cfg = await authConfig();
        if (!cfg.sso) throw new Error('SSO is not enabled on this server');
        const u = await userManager(cfg.sso).signinCallback();
        if (!u) throw new Error('No sign-in response');
        await loginWithToken(apiTokenFrom(u, cfg.sso));
        if (!cancelled) window.history.replaceState(null, '', '/');
      } catch (e) { if (!cancelled) setError((e as Error).message); }
    })();
    return () => { cancelled = true; };
  }, [loginWithToken]);
  return (
    <div className="login"><div className="panel form"><h1>RFID Platform</h1>
      {error ? <><div className="error">{error}</div><a href="/">Back to sign-in</a></> : <p className="muted">Completing single sign-on…</p>}
    </div></div>
  );
}
