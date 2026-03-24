import { useEffect, useRef, useState, useCallback } from 'react';
import { settingsApi, filesystemApi, type AppSettings, type BrowseResult } from '../api/client';

// ── Section component ──────────────────────────────────────────────────────────

function SettingsSection({
  icon,
  title,
  children,
}: {
  icon: string;
  title: string;
  children: React.ReactNode;
}) {
  return (
    <div className="bg-surface-container-low rounded-xl border border-white/5 overflow-hidden mb-6">
      <div className="flex items-center gap-3 px-5 py-4 border-b border-white/5">
        <span className="material-symbols-outlined text-primary text-xl">{icon}</span>
        <h2 className="font-bold text-on-surface text-sm">{title}</h2>
      </div>
      <div className="p-5 grid grid-cols-1 md:grid-cols-2 gap-4">{children}</div>
    </div>
  );
}

// ── Plain text / number field ─────────────────────────────────────────────────

function Field({
  label,
  value,
  type = 'text',
  onChange,
}: {
  label: string;
  value: string;
  type?: 'text' | 'number';
  onChange: (v: string) => void;
}) {
  return (
    <div>
      <label className="block mb-1 text-[10px] uppercase tracking-widest text-on-surface-variant font-bold">
        {label}
      </label>
      <input
        type={type}
        className="w-full bg-surface-container-highest border border-outline-variant/20 rounded-lg py-2.5 px-4 text-on-surface text-sm focus:outline-none focus:ring-1 focus:ring-primary/50 transition-all placeholder:text-on-surface-variant/50"
        value={value}
        onChange={e => onChange(e.target.value)}
      />
    </div>
  );
}

// ── Directory Browser Modal ────────────────────────────────────────────────────

function DirectoryBrowserModal({
  initialPath,
  onSelect,
  onClose,
}: {
  initialPath: string;
  onSelect: (path: string) => void;
  onClose: () => void;
}) {
  const [browse, setBrowse] = useState<BrowseResult | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [pathInput, setPathInput] = useState(initialPath);
  const pathInputRef = useRef<HTMLInputElement>(null);

  const navigate = useCallback(async (path?: string) => {
    setLoading(true);
    setLoadError(null);
    try {
      const result = await filesystemApi.browse(path);
      setBrowse(result);
      setPathInput(result.currentPath);
    } catch (e) {
      setLoadError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, []);

  // Open at the current field value (or server cwd if empty)
  useEffect(() => { navigate(initialPath || undefined); }, [navigate]);  // eslint-disable-line react-hooks/exhaustive-deps

  function handlePathInputKey(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'Enter') navigate(pathInput);
  }

  const sep = browse?.currentPath.includes('\\') ? '\\' : '/';

  /** Breadcrumb segments from the current path */
  function breadcrumbs() {
    if (!browse) return [];
    const parts = browse.currentPath.split(/[\\/]/).filter(Boolean);
    // Windows: first part is "C:" etc.
    return parts.map((part, i) => {
      const fullPath = browse.currentPath.startsWith('/')
        ? '/' + parts.slice(0, i + 1).join('/')
        : parts.slice(0, i + 1).join(sep) + (i === 0 ? sep : '');
      return { label: part, fullPath };
    });
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm p-4">
      <div className="bg-surface-container rounded-xl w-full max-w-2xl shadow-[0px_10px_30px_rgba(6,14,32,0.5)] border border-outline-variant/[0.15] flex flex-col max-h-[80vh]">

        {/* Header */}
        <div className="flex items-center justify-between px-5 py-4 border-b border-white/5 shrink-0">
          <h2 className="text-sm font-bold flex items-center gap-2">
            <span className="material-symbols-outlined text-primary text-lg">folder_open</span>
            Select Directory
          </h2>
          <button onClick={onClose} className="text-on-surface-variant hover:text-on-surface transition-colors material-symbols-outlined text-lg">
            close
          </button>
        </div>

        {/* Path bar */}
        <div className="flex items-center gap-2 px-4 py-3 border-b border-white/5 bg-surface-container-highest/30 shrink-0">
          <button
            onClick={() => browse?.parentPath ? navigate(browse.parentPath) : undefined}
            disabled={!browse?.parentPath || loading}
            className="shrink-0 flex items-center justify-center w-8 h-8 rounded-lg text-on-surface-variant hover:bg-white/10 disabled:opacity-30 disabled:cursor-not-allowed transition-colors material-symbols-outlined text-lg"
            title="Go up"
          >
            arrow_upward
          </button>
          <input
            ref={pathInputRef}
            value={pathInput}
            onChange={e => setPathInput(e.target.value)}
            onKeyDown={handlePathInputKey}
            onBlur={() => { if (pathInput !== browse?.currentPath) navigate(pathInput); }}
            className="flex-1 min-w-0 bg-transparent border-b border-outline-variant/30 text-on-surface text-sm py-1 px-1 focus:outline-none focus:border-primary/60 font-mono"
            spellCheck={false}
          />
          <button
            onClick={() => navigate(pathInput)}
            disabled={loading}
            className="shrink-0 flex items-center justify-center w-8 h-8 rounded-lg text-on-surface-variant hover:bg-white/10 disabled:opacity-30 transition-colors material-symbols-outlined text-lg"
            title="Navigate"
          >
            arrow_forward
          </button>
        </div>

        {/* Windows drives (when available) */}
        {browse?.drives && (
          <div className="flex items-center gap-1 px-4 py-2 border-b border-white/5 bg-surface-container-highest/10 shrink-0 overflow-x-auto">
            <span className="text-[10px] uppercase tracking-widest text-on-surface-variant font-bold mr-1 shrink-0">Drives:</span>
            {browse.drives.map(drive => (
              <button
                key={drive}
                onClick={() => navigate(drive)}
                className="shrink-0 px-2.5 py-1 rounded text-xs font-mono text-on-surface-variant hover:bg-white/10 hover:text-on-surface transition-colors border border-white/10"
              >
                {drive}
              </button>
            ))}
          </div>
        )}

        {/* Breadcrumb (optional, skip on root) */}
        {browse && breadcrumbs().length > 0 && (
          <div className="flex items-center gap-1 px-4 py-2 border-b border-white/5 shrink-0 overflow-x-auto text-xs text-on-surface-variant">
            {breadcrumbs().map((crumb, i, arr) => (
              <span key={crumb.fullPath} className="flex items-center gap-1 shrink-0">
                {i > 0 && <span className="opacity-30">/</span>}
                <button
                  onClick={() => navigate(crumb.fullPath)}
                  className={`hover:text-on-surface transition-colors ${i === arr.length - 1 ? 'text-primary font-semibold' : ''}`}
                >
                  {crumb.label}
                </button>
              </span>
            ))}
          </div>
        )}

        {/* Directory list */}
        <div className="flex-1 overflow-y-auto min-h-0 px-2 py-2">
          {loading && (
            <div className="flex items-center justify-center py-10">
              <span className="material-symbols-outlined text-on-surface-variant text-3xl animate-spin">progress_activity</span>
            </div>
          )}
          {!loading && loadError && (
            <div className="text-error text-sm p-4 flex items-center gap-2">
              <span className="material-symbols-outlined text-base">error</span>
              {loadError}
            </div>
          )}
          {!loading && !loadError && browse && browse.directories.length === 0 && (
            <p className="text-center text-on-surface-variant/60 text-sm py-10">No subdirectories</p>
          )}
          {!loading && !loadError && browse?.directories.map(dir => (
            <button
              key={dir.fullPath}
              onClick={() => navigate(dir.fullPath)}
              className="w-full flex items-center gap-3 px-3 py-2.5 rounded-lg text-left text-sm text-on-surface hover:bg-white/5 transition-colors group"
            >
              <span className="material-symbols-outlined text-primary/70 group-hover:text-primary text-lg shrink-0">folder</span>
              <span className="truncate">{dir.name}</span>
            </button>
          ))}
        </div>

        {/* Footer */}
        <div className="shrink-0 px-5 py-4 border-t border-white/5 flex items-center justify-between gap-3">
          <p className="text-xs text-on-surface-variant truncate font-mono min-w-0">
            {browse?.currentPath ?? '…'}
          </p>
          <div className="flex gap-3 shrink-0">
            <button
              onClick={onClose}
              className="px-4 py-2 rounded-lg text-sm font-semibold text-on-surface-variant hover:bg-surface-bright transition-colors"
            >
              Cancel
            </button>
            <button
              onClick={() => browse && onSelect(browse.currentPath)}
              disabled={!browse}
              className="flex items-center gap-2 px-5 py-2 rounded-lg text-sm font-bold bg-primary text-on-primary hover:bg-primary/90 disabled:opacity-40 transition-all"
            >
              <span className="material-symbols-outlined text-base">check</span>
              Select This Folder
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

// ── Directory field (text + browse button) ────────────────────────────────────

function DirField({
  label,
  value,
  onChange,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
}) {
  const [open, setOpen] = useState(false);

  return (
    <>
      <div>
        <label className="block mb-1 text-[10px] uppercase tracking-widest text-on-surface-variant font-bold">
          {label}
        </label>
        <div className="flex items-stretch gap-2">
          <input
            type="text"
            className="flex-1 min-w-0 bg-surface-container-highest border border-outline-variant/20 rounded-lg py-2.5 px-4 text-on-surface text-sm focus:outline-none focus:ring-1 focus:ring-primary/50 transition-all font-mono placeholder:text-on-surface-variant/50"
            value={value}
            onChange={e => onChange(e.target.value)}
          />
          <button
            type="button"
            onClick={() => setOpen(true)}
            className="shrink-0 flex items-center justify-center w-10 rounded-lg bg-surface-container-highest border border-outline-variant/20 text-on-surface-variant hover:text-primary hover:border-primary/40 hover:bg-primary/5 transition-all material-symbols-outlined text-lg"
            title="Browse…"
          >
            folder_open
          </button>
        </div>
      </div>

      {open && (
        <DirectoryBrowserModal
          initialPath={value}
          onSelect={path => { onChange(path); setOpen(false); }}
          onClose={() => setOpen(false)}
        />
      )}
    </>
  );
}

// ── Page ───────────────────────────────────────────────────────────────────────

export default function SettingsPage() {
  const [settings, setSettings] = useState<AppSettings | null>(null);
  const [draft, setDraft] = useState<AppSettings | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      setError(null);
      const data = await settingsApi.get();
      setSettings(data);
      setDraft(data);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  function set<K extends keyof AppSettings>(key: K, value: AppSettings[K]) {
    setDraft(prev => prev ? { ...prev, [key]: value } : prev);
  }

  async function handleSave() {
    if (!draft) return;
    setSaving(true);
    setSaved(false);
    try {
      const updated = await settingsApi.save(draft);
      setSettings(updated);
      setDraft(updated);
      setSaved(true);
      setTimeout(() => setSaved(false), 3000);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSaving(false);
    }
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center py-20">
        <span className="material-symbols-outlined text-4xl text-on-surface-variant animate-spin">progress_activity</span>
      </div>
    );
  }

  if (error) {
    return (
      <div className="bg-error/10 border border-error/20 rounded-xl p-6 text-error text-sm">
        <span className="material-symbols-outlined mr-2 align-middle">error</span>
        {error}
      </div>
    );
  }

  if (!draft) return null;

  const isDirty = JSON.stringify(draft) !== JSON.stringify(settings);

  return (
    <>
      {/* Header */}
      <div className="mb-8 flex items-start justify-between">
        <div>
          <h1 className="text-3xl font-bold tracking-tight text-on-surface mb-1">Settings</h1>
          <p className="text-on-surface-variant text-sm">Configure Vod2Tube behaviour</p>
        </div>
        <button
          onClick={handleSave}
          disabled={!isDirty || saving}
          className={`flex items-center gap-2 px-5 py-2.5 rounded-lg text-sm font-bold transition-all
            ${isDirty && !saving
              ? 'bg-primary text-on-primary hover:bg-primary/90 cursor-pointer'
              : 'bg-surface-container text-on-surface-variant cursor-not-allowed opacity-50'
            }`}
        >
          <span className="material-symbols-outlined text-base">
            {saving ? 'progress_activity' : saved ? 'check_circle' : 'save'}
          </span>
          {saving ? 'Saving…' : saved ? 'Saved!' : 'Save Changes'}
        </button>
      </div>

      {/* Tool Paths */}
      <SettingsSection icon="terminal" title="Tool Paths">
        <Field
          label="TwitchDownloaderCLI Path"
          value={draft.twitchDownloaderCliPath}
          onChange={v => set('twitchDownloaderCliPath', v)}
        />
        <Field
          label="FFmpeg Path"
          value={draft.ffmpegPath}
          onChange={v => set('ffmpegPath', v)}
        />
        <Field
          label="FFprobe Path"
          value={draft.ffprobePath}
          onChange={v => set('ffprobePath', v)}
        />
        <Field
          label="yt-dlp Path"
          value={draft.ytDlpPath}
          onChange={v => set('ytDlpPath', v)}
        />
      </SettingsSection>

      {/* Storage Paths */}
      <SettingsSection icon="folder" title="Storage Paths">
        <DirField
          label="VOD Download Directory"
          value={draft.vodDownloadDir}
          onChange={v => set('vodDownloadDir', v)}
        />
        <DirField
          label="VOD Download Temp Directory"
          value={draft.vodDownloadTempDir}
          onChange={v => set('vodDownloadTempDir', v)}
        />
        <DirField
          label="Chat Render Directory"
          value={draft.chatRenderDir}
          onChange={v => set('chatRenderDir', v)}
        />
        <DirField
          label="Chat Render Temp Directory"
          value={draft.chatRenderTempDir}
          onChange={v => set('chatRenderTempDir', v)}
        />
        <DirField
          label="Final Video Directory"
          value={draft.finalVideoDir}
          onChange={v => set('finalVideoDir', v)}
        />
      </SettingsSection>

      {/* Chat Rendering */}
      <SettingsSection icon="chat" title="Chat Rendering">
        <Field
          label="Chat Panel Width (px)"
          value={String(draft.chatWidth)}
          type="number"
          onChange={v => set('chatWidth', Number(v))}
        />
        <Field
          label="Font Size (pt)"
          value={String(draft.chatFontSize)}
          type="number"
          onChange={v => set('chatFontSize', Number(v))}
        />
        <Field
          label="Update Rate"
          value={String(draft.chatUpdateRate)}
          type="number"
          onChange={v => set('chatUpdateRate', Number(v))}
        />
      </SettingsSection>
    </>
  );
}

