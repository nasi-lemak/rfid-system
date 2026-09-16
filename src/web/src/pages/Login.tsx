import { useEffect, useState, type FormEvent } from 'react';
import { useAuth } from '../auth';
import { authConfig, userManager, type AuthConfig } from '../sso';
import { LANGS, useI18n } from '../i18n';

export default function Login() {
  const { login } = useAuth();
  const { t, lang, setLang } = useI18n();
  const [email, setEmail] = useState('admin@demo.local');
  const [password, setPassword] = useState('admin123');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [cfg, setCfg] = useState<AuthConfig | null>(null);
  useEffect(() => { authConfig().then(setCfg); }, []);
  const sso = async () => { if (!cfg?.sso) return; setBusy(true); setError(null); try { await userManager(cfg.sso).signinRedirect(); } catch (err) { setError((err as Error).message); setBusy(false); } };
  const submit = async (e: FormEvent) => { e.preventDefault(); setBusy(true); setError(null); try { await login(email, password); } catch (err) { setError((err as Error).message); } finally { setBusy(false); } };
  return (
    <div className="login">
      <form className="panel form" onSubmit={submit}>
        <h1>RFID Platform</h1>
        <label>{t('Email')}<input value={email} onChange={(e) => setEmail(e.target.value)} autoFocus /></label>
        <label>{t('Password')}<input type="password" value={password} onChange={(e) => setPassword(e.target.value)} /></label>
        {error && <div className="error">{error}</div>}
        <button className="primary" disabled={busy}>{busy ? t('Signing in…') : t('Sign in')}</button>
        {cfg?.sso && <button type="button" disabled={busy} onClick={sso} title={cfg.sso.authority}>{t('Sign in with SSO')}</button>}
        <p className="muted small">Demo: admin@demo.local / admin123 · operator@demo.local / operator123</p>
        <select value={lang} onChange={(e) => setLang(e.target.value as typeof lang)} title={t('Language')}>{LANGS.map((l) => <option key={l.code} value={l.code}>{l.label}</option>)}</select>
      </form>
    </div>
  );
}
