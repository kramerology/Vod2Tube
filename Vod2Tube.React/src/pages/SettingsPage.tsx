import { useEffect, useState, useCallback } from 'react';
import { settingsApi, type AppSettings } from '../api/client';

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

// ── Field component ────────────────────────────────────────────────────────────

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
        <Field
          label="VOD Download Directory"
          value={draft.vodDownloadDir}
          onChange={v => set('vodDownloadDir', v)}
        />
        <Field
          label="VOD Download Temp Directory"
          value={draft.vodDownloadTempDir}
          onChange={v => set('vodDownloadTempDir', v)}
        />
        <Field
          label="Chat Render Directory"
          value={draft.chatRenderDir}
          onChange={v => set('chatRenderDir', v)}
        />
        <Field
          label="Chat Render Temp Directory"
          value={draft.chatRenderTempDir}
          onChange={v => set('chatRenderTempDir', v)}
        />
        <Field
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

