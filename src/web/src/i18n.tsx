import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';

export type Lang = 'en' | 'ms' | 'zh' | 'es' | 'de' | 'fr';
export const LANGS: { code: Lang; label: string }[] = [{ code: 'en', label: 'English' }, { code: 'ms', label: 'Bahasa Melayu' }, { code: 'zh', label: '中文' }, { code: 'es', label: 'Español' }, { code: 'de', label: 'Deutsch' }, { code: 'fr', label: 'Français' }];

/**
 * Translation tables keyed by the English source text. Missing keys fall back to English, so screens can be
 * translated incrementally; `t()` also handles simple `{n}` placeholders.
 */
const dict: Record<Lang, Record<string, string>> = {
  en: {},
  ms: {
    'Dashboard': 'Papan pemuka', 'Live reads': 'Bacaan langsung', 'Alerts': 'Amaran', 'Presence & location': 'Kehadiran & lokasi', 'Map & geofences': 'Peta & geopagar', 'Event log': 'Log peristiwa', 'Reports': 'Laporan', 'Analytics': 'Analitik', 'Anomalies': 'Anomali', 'Billing': 'Pengebilan', 'Maintenance': 'Penyelenggaraan',
    'Items': 'Item', 'Tags': 'Tag', 'Operations': 'Operasi', 'Stocktakes': 'Kiraan stok', 'Print queue': 'Giliran cetak', 'Encoding': 'Pengekodan tag',
    'Item types': 'Jenis item', 'Locations': 'Lokasi', 'Parties': 'Pihak', 'Readers & devices': 'Pembaca & peranti', 'Rules': 'Peraturan', 'Notifications': 'Pemberitahuan', 'Label designer': 'Pereka label', 'Solution templates': 'Templat penyelesaian', 'Integrations': 'Integrasi', 'ERP import': 'Import ERP', 'Users': 'Pengguna', 'Cluster & system': 'Kluster & sistem', 'Audit & retention': 'Audit & pengekalan',
    'Overview': 'Gambaran', 'Track': 'Jejak', 'Configure': 'Konfigurasi', 'Sign out': 'Log keluar', 'Sign in': 'Log masuk', 'Sign in with SSO': 'Log masuk dengan SSO', 'Email': 'E-mel', 'Password': 'Kata laluan', 'Signing in…': 'Sedang log masuk…', 'Language': 'Bahasa',
    'Save': 'Simpan', 'Cancel': 'Batal', 'Edit': 'Sunting', 'Delete': 'Padam', 'Close': 'Tutup', 'Search': 'Cari', 'Refresh': 'Muat semula', 'Loading…': 'Memuatkan…', 'No data': 'Tiada data',
    'Tracked items': 'Item dijejak', 'Open alerts': 'Amaran terbuka', 'Critical alerts': 'Amaran kritikal', 'Readers offline': 'Pembaca luar talian', 'In custody': 'Dalam jagaan', 'Overdue returns': 'Pemulangan lewat', 'Missing': 'Hilang', 'Reads today': 'Bacaan hari ini', 'Items by type': 'Item mengikut jenis', 'Items by state': 'Item mengikut keadaan', 'Items by location': 'Item mengikut lokasi', 'Reader health': 'Kesihatan pembaca', 'Recent activity': 'Aktiviti terkini', 'Auto-refreshing': 'Muat semula automatik', 'Live connection up': 'Sambungan langsung aktif', 'Live connection down': 'Sambungan langsung terputus',
  },
  zh: {
    'Dashboard': '仪表盘', 'Live reads': '实时读取', 'Alerts': '告警', 'Presence & location': '在场与位置', 'Map & geofences': '地图与围栏', 'Event log': '事件日志', 'Reports': '报表', 'Analytics': '分析', 'Anomalies': '异常', 'Billing': '计费', 'Maintenance': '维护',
    'Items': '物品', 'Tags': '标签', 'Operations': '作业', 'Stocktakes': '盘点', 'Print queue': '打印队列', 'Encoding': '标签编码',
    'Item types': '物品类型', 'Locations': '位置', 'Parties': '相关方', 'Readers & devices': '读写器与设备', 'Rules': '规则', 'Notifications': '通知', 'Label designer': '标签设计', 'Solution templates': '解决方案模板', 'Integrations': '集成', 'ERP import': 'ERP 导入', 'Users': '用户', 'Cluster & system': '集群与系统', 'Audit & retention': '审计与保留',
    'Overview': '概览', 'Track': '追踪', 'Configure': '配置', 'Sign out': '退出', 'Sign in': '登录', 'Sign in with SSO': '使用 SSO 登录', 'Email': '邮箱', 'Password': '密码', 'Signing in…': '正在登录…', 'Language': '语言',
    'Save': '保存', 'Cancel': '取消', 'Edit': '编辑', 'Delete': '删除', 'Close': '关闭', 'Search': '搜索', 'Refresh': '刷新', 'Loading…': '加载中…', 'No data': '无数据',
    'Tracked items': '追踪物品', 'Open alerts': '未处理告警', 'Critical alerts': '严重告警', 'Readers offline': '离线读写器', 'In custody': '在保管中', 'Overdue returns': '逾期未还', 'Missing': '缺失', 'Reads today': '今日读取', 'Items by type': '按类型', 'Items by state': '按状态', 'Items by location': '按位置', 'Reader health': '读写器健康', 'Recent activity': '最近活动', 'Auto-refreshing': '自动刷新', 'Live connection up': '实时连接正常', 'Live connection down': '实时连接断开',
  },
  es: {
    'Dashboard': 'Panel', 'Live reads': 'Lecturas en vivo', 'Alerts': 'Alertas', 'Presence & location': 'Presencia y ubicación', 'Map & geofences': 'Mapa y geocercas', 'Event log': 'Registro de eventos', 'Reports': 'Informes', 'Analytics': 'Analítica', 'Anomalies': 'Anomalías', 'Billing': 'Facturación', 'Maintenance': 'Mantenimiento',
    'Items': 'Artículos', 'Tags': 'Etiquetas', 'Operations': 'Operaciones', 'Stocktakes': 'Recuentos', 'Print queue': 'Cola de impresión', 'Encoding': 'Codificación',
    'Item types': 'Tipos de artículo', 'Locations': 'Ubicaciones', 'Parties': 'Terceros', 'Readers & devices': 'Lectores y dispositivos', 'Rules': 'Reglas', 'Notifications': 'Notificaciones', 'Label designer': 'Diseñador de etiquetas', 'Solution templates': 'Plantillas', 'Integrations': 'Integraciones', 'ERP import': 'Importar ERP', 'Users': 'Usuarios', 'Cluster & system': 'Clúster y sistema', 'Audit & retention': 'Auditoría y retención',
    'Overview': 'Resumen', 'Track': 'Seguimiento', 'Configure': 'Configurar', 'Sign out': 'Cerrar sesión', 'Sign in': 'Iniciar sesión', 'Sign in with SSO': 'Iniciar sesión con SSO', 'Email': 'Correo', 'Password': 'Contraseña', 'Signing in…': 'Iniciando sesión…', 'Language': 'Idioma',
    'Save': 'Guardar', 'Cancel': 'Cancelar', 'Edit': 'Editar', 'Delete': 'Eliminar', 'Close': 'Cerrar', 'Search': 'Buscar', 'Refresh': 'Actualizar', 'Loading…': 'Cargando…', 'No data': 'Sin datos',
    'Tracked items': 'Artículos rastreados', 'Open alerts': 'Alertas abiertas', 'Critical alerts': 'Alertas críticas', 'Readers offline': 'Lectores sin conexión', 'In custody': 'En custodia', 'Overdue returns': 'Devoluciones vencidas', 'Missing': 'Perdidos', 'Reads today': 'Lecturas hoy', 'Items by type': 'Por tipo', 'Items by state': 'Por estado', 'Items by location': 'Por ubicación', 'Reader health': 'Salud de lectores', 'Recent activity': 'Actividad reciente', 'Auto-refreshing': 'Actualización automática', 'Live connection up': 'Conexión en vivo activa', 'Live connection down': 'Conexión en vivo caída',
  },
  de: {
    'Dashboard': 'Übersicht', 'Live reads': 'Live-Lesungen', 'Alerts': 'Alarme', 'Presence & location': 'Präsenz & Ort', 'Map & geofences': 'Karte & Geofences', 'Event log': 'Ereignisprotokoll', 'Reports': 'Berichte', 'Analytics': 'Analysen', 'Anomalies': 'Anomalien', 'Billing': 'Abrechnung', 'Maintenance': 'Wartung',
    'Items': 'Objekte', 'Tags': 'Tags', 'Operations': 'Vorgänge', 'Stocktakes': 'Inventuren', 'Print queue': 'Druckwarteschlange', 'Encoding': 'Codierung',
    'Item types': 'Objekttypen', 'Locations': 'Orte', 'Parties': 'Parteien', 'Readers & devices': 'Reader & Geräte', 'Rules': 'Regeln', 'Notifications': 'Benachrichtigungen', 'Label designer': 'Etikettendesigner', 'Solution templates': 'Lösungsvorlagen', 'Integrations': 'Integrationen', 'ERP import': 'ERP-Import', 'Users': 'Benutzer', 'Cluster & system': 'Cluster & System', 'Audit & retention': 'Audit & Aufbewahrung',
    'Overview': 'Überblick', 'Track': 'Verfolgen', 'Configure': 'Konfigurieren', 'Sign out': 'Abmelden', 'Sign in': 'Anmelden', 'Sign in with SSO': 'Mit SSO anmelden', 'Email': 'E-Mail', 'Password': 'Passwort', 'Signing in…': 'Anmeldung…', 'Language': 'Sprache',
    'Save': 'Speichern', 'Cancel': 'Abbrechen', 'Edit': 'Bearbeiten', 'Delete': 'Löschen', 'Close': 'Schließen', 'Search': 'Suchen', 'Refresh': 'Aktualisieren', 'Loading…': 'Wird geladen…', 'No data': 'Keine Daten',
    'Tracked items': 'Verfolgte Objekte', 'Open alerts': 'Offene Alarme', 'Critical alerts': 'Kritische Alarme', 'Readers offline': 'Reader offline', 'In custody': 'In Verwahrung', 'Overdue returns': 'Überfällige Rückgaben', 'Missing': 'Fehlend', 'Reads today': 'Lesungen heute', 'Items by type': 'Nach Typ', 'Items by state': 'Nach Zustand', 'Items by location': 'Nach Ort', 'Reader health': 'Reader-Zustand', 'Recent activity': 'Letzte Aktivität', 'Auto-refreshing': 'Automatische Aktualisierung', 'Live connection up': 'Live-Verbindung aktiv', 'Live connection down': 'Live-Verbindung unterbrochen',
  },
  fr: {
    'Dashboard': 'Tableau de bord', 'Live reads': 'Lectures en direct', 'Alerts': 'Alertes', 'Presence & location': 'Présence et localisation', 'Map & geofences': 'Carte et géorepères', 'Event log': "Journal d'événements", 'Reports': 'Rapports', 'Analytics': 'Analytique', 'Anomalies': 'Anomalies', 'Billing': 'Facturation', 'Maintenance': 'Maintenance',
    'Items': 'Articles', 'Tags': 'Tags', 'Operations': 'Opérations', 'Stocktakes': 'Inventaires', 'Print queue': "File d'impression", 'Encoding': 'Encodage',
    'Item types': "Types d'article", 'Locations': 'Emplacements', 'Parties': 'Tiers', 'Readers & devices': 'Lecteurs et appareils', 'Rules': 'Règles', 'Notifications': 'Notifications', 'Label designer': "Éditeur d'étiquettes", 'Solution templates': 'Modèles de solution', 'Integrations': 'Intégrations', 'ERP import': 'Import ERP', 'Users': 'Utilisateurs', 'Cluster & system': 'Cluster et système', 'Audit & retention': 'Audit et rétention',
    'Overview': "Vue d'ensemble", 'Track': 'Suivi', 'Configure': 'Configurer', 'Sign out': 'Déconnexion', 'Sign in': 'Connexion', 'Sign in with SSO': 'Connexion SSO', 'Email': 'E-mail', 'Password': 'Mot de passe', 'Signing in…': 'Connexion…', 'Language': 'Langue',
    'Save': 'Enregistrer', 'Cancel': 'Annuler', 'Edit': 'Modifier', 'Delete': 'Supprimer', 'Close': 'Fermer', 'Search': 'Rechercher', 'Refresh': 'Actualiser', 'Loading…': 'Chargement…', 'No data': 'Aucune donnée',
    'Tracked items': 'Articles suivis', 'Open alerts': 'Alertes ouvertes', 'Critical alerts': 'Alertes critiques', 'Readers offline': 'Lecteurs hors ligne', 'In custody': 'En garde', 'Overdue returns': 'Retours en retard', 'Missing': 'Manquants', 'Reads today': "Lectures aujourd'hui", 'Items by type': 'Par type', 'Items by state': 'Par état', 'Items by location': 'Par emplacement', 'Reader health': 'Santé des lecteurs', 'Recent activity': 'Activité récente', 'Auto-refreshing': 'Actualisation automatique', 'Live connection up': 'Connexion en direct active', 'Live connection down': 'Connexion en direct coupée',
  },
};

interface I18n { lang: Lang; setLang: (l: Lang) => void; t: (text: string, vars?: Record<string, string | number>) => string; locale: string }
const Ctx = createContext<I18n>(null!);
const locales: Record<Lang, string> = { en: 'en-GB', ms: 'ms-MY', zh: 'zh-CN', es: 'es-ES', de: 'de-DE', fr: 'fr-FR' };

export function I18nProvider({ children }: { children: ReactNode }) {
  const [lang, setLangState] = useState<Lang>(() => { try { const l = localStorage.getItem('rfid.lang') as Lang | null; return l && dict[l] ? l : (navigator.language.slice(0, 2) as Lang) in dict ? (navigator.language.slice(0, 2) as Lang) : 'en'; } catch { return 'en'; } });
  const setLang = useCallback((l: Lang) => { setLangState(l); try { localStorage.setItem('rfid.lang', l); } catch { /* ignore */ } document.documentElement.lang = l; }, []);
  const t = useCallback((text: string, vars?: Record<string, string | number>) => { let out = dict[lang]?.[text] ?? text; if (vars) for (const [k, v] of Object.entries(vars)) out = out.replace(`{${k}}`, String(v)); return out; }, [lang]);
  const value = useMemo(() => ({ lang, setLang, t, locale: locales[lang] }), [lang, setLang, t]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}
export const useI18n = () => useContext(Ctx);
export const useT = () => useContext(Ctx).t;
/** Languages the UI ships with, for the selector and for the `lang` claim/preference. */
export const languageLabel = (l: Lang) => LANGS.find((x) => x.code === l)?.label ?? l;
