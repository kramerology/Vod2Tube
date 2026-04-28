import { useEffect, useState } from 'react';
import { channelsApi, accountsApi, type Channel, type ChannelQueueStatus, type YouTubeAccount } from '../api/client';

function queueStateLabel(channel: ChannelQueueStatus): string {
  if (!channel.active) return 'Paused';
  if (channel.currentJobFailed) return `Needs attention${channel.currentStage ? ` · ${channel.currentStage}` : ''}`;
  if (channel.currentJobPaused) return `Current VOD paused${channel.currentStage ? ` · ${channel.currentStage}` : ''}`;
  if (channel.currentVodId) return `Processing oldest VOD${channel.currentStage ? ` · ${channel.currentStage}` : ''}`;
  return 'Waiting to fetch oldest VOD';
}

function queueStateTone(channel: ChannelQueueStatus): string {
  if (!channel.active) return 'text-on-surface-variant';
  if (channel.currentJobFailed) return 'text-error';
  if (channel.currentVodId) return 'text-primary';
  return 'text-on-surface-variant';
}

function formatRelativeTime(iso: string | null): string {
  if (!iso) return 'No uploads yet';

  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) return 'No uploads yet';

  const diffMs = Date.now() - then;
  const minute = 60 * 1000;
  const hour = 60 * minute;
  const day = 24 * hour;

  if (diffMs < hour) {
    const minutes = Math.max(1, Math.floor(diffMs / minute));
    return `${minutes} minute${minutes === 1 ? '' : 's'} ago`;
  }

  if (diffMs < day) {
    const hours = Math.floor(diffMs / hour);
    return `${hours} hour${hours === 1 ? '' : 's'} ago`;
  }

  const days = Math.floor(diffMs / day);
  return `${days} day${days === 1 ? '' : 's'} ago`;
}

// ── Add / Edit dialog ─────────────────────────────────────────────────────────

function ChannelDialog({
  channel,
  accounts,
  onSave,
  onClose,
}: {
  channel: Partial<Channel>;
  accounts: YouTubeAccount[];
  onSave: (c: Partial<Channel>) => Promise<void>;
  onClose: () => void;
}) {
  const [name, setName] = useState(channel.channelName ?? '');
  const [active, setActive] = useState(channel.active ?? true);
  const [accountId, setAccountId] = useState<number | null>(channel.youTubeAccountId ?? null);
  const [saving, setSaving] = useState(false);

  async function handleSave() {
    if (!name.trim()) return;
    setSaving(true);
    await onSave({ ...channel, channelName: name.trim(), active, youTubeAccountId: accountId });
    setSaving(false);
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm">
      <div className="bg-surface-container rounded-xl w-full max-w-md p-6 shadow-[0px_10px_30px_rgba(6,14,32,0.5)] border border-outline-variant/[0.15]">
        <h2 className="text-lg font-bold mb-6 flex items-center gap-2">
          <span className="material-symbols-outlined text-primary">{channel.id ? 'edit' : 'person_add'}</span>
          {channel.id ? 'Edit Channel' : 'Add Channel'}
        </h2>

        <label className="block mb-1 text-[10px] uppercase tracking-widest text-on-surface-variant font-bold">
          Twitch Username
        </label>
        <div className="relative mb-5">
          <span className="absolute inset-y-0 left-3 flex items-center text-on-surface-variant material-symbols-outlined text-lg">
            tv
          </span>
          <input
            className="w-full bg-surface-container-highest border border-outline-variant/20 rounded-lg py-3 pl-10 pr-4 text-on-surface focus:outline-none focus:ring-1 focus:ring-primary/50 transition-all placeholder:text-on-surface-variant/50"
            placeholder="e.g. pirate_software"
            value={name}
            onChange={e => setName(e.target.value)}
            onKeyDown={e => e.key === 'Enter' && handleSave()}
          />
        </div>

        <label className="flex items-center gap-3 cursor-pointer mb-5 select-none">
          <span className="relative inline-flex items-center cursor-pointer">
            <input
              type="checkbox"
              className="sr-only peer"
              checked={active}
              onChange={() => setActive(v => !v)}
            />
            <div className="w-11 h-6 bg-surface-container-highest rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:start-[2px] after:bg-white after:border-gray-300 after:border after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-primary" />
          </span>
          <span className="text-sm text-on-surface-variant">
            {active ? 'Active — monitor for new VODs' : 'Paused'}
          </span>
        </label>

        <label className="block mb-1 text-[10px] uppercase tracking-widest text-on-surface-variant font-bold">
          YouTube Account
        </label>
        <div className="relative mb-6">
          <span className="absolute inset-y-0 left-3 flex items-center text-on-surface-variant material-symbols-outlined text-lg">
            smart_display
          </span>
          <select
            className="w-full bg-surface-container-highest border border-outline-variant/20 rounded-lg py-3 pl-10 pr-4 text-on-surface focus:outline-none focus:ring-1 focus:ring-primary/50 transition-all appearance-none cursor-pointer"
            value={accountId ?? ''}
            onChange={e => setAccountId(e.target.value ? Number(e.target.value) : null)}
          >
            <option value="">No account (legacy default)</option>
            {accounts.map(a => (
              <option key={a.id} value={a.id}>
                {a.name}{a.channelTitle ? ` — ${a.channelTitle}` : ''}{a.isAuthorized ? '' : ' ⚠️ Not authorized'}
              </option>
            ))}
          </select>
          <span className="absolute inset-y-0 right-3 flex items-center text-on-surface-variant material-symbols-outlined text-lg pointer-events-none">
            expand_more
          </span>
        </div>

        <div className="flex justify-end gap-3">
          <button
            onClick={onClose}
            className="px-5 py-2 rounded-lg text-sm font-semibold text-on-surface-variant hover:bg-surface-bright transition-colors"
          >
            Cancel
          </button>
          <button
            onClick={handleSave}
            disabled={!name.trim() || saving}
            className="px-6 py-2 rounded-lg text-sm font-bold bg-gradient-to-br from-primary to-primary-container text-on-primary-container hover:opacity-90 disabled:opacity-40 transition-all shadow-lg shadow-primary/10"
          >
            {saving ? 'Saving…' : 'Save'}
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Wide Channel Row Card ─────────────────────────────────────────────────────

function ChannelRow({
  channel,
  avatarUrl,
  accountName,
  onEdit,
  onToggle,
  onQueueNext,
  onDelete,
}: {
  channel: ChannelQueueStatus;
  avatarUrl?: string;
  accountName?: string;
  onEdit: () => void;
  onToggle: () => void;
  onQueueNext: () => void;
  onDelete: () => void;
}) {
  return (
    <div
      className={`bg-surface-container-low group hover:bg-surface-container transition-all duration-300 rounded-xl p-4 flex items-center gap-6 ${
        channel.active ? 'border-l-2 border-primary' : 'border-l-2 border-transparent'
      }`}
    >
      {/* Thumbnail / Avatar */}
      <div className="relative w-48 h-24 rounded-lg overflow-hidden flex-shrink-0">
        {avatarUrl ? (
          <img
            src={avatarUrl}
            alt={channel.channelName}
            className="w-full h-full object-cover grayscale group-hover:grayscale-0 transition-all duration-500"
          />
        ) : (
          <div
            className="w-full h-full"
            style={{ background: 'linear-gradient(135deg, #131b2e 0%, #222a3d 100%)' }}
          />
        )}
        <div className="absolute inset-0 bg-gradient-to-t from-black/80 to-transparent" />
        <div className="absolute bottom-2 left-2 flex items-center gap-1.5">
          <span className={`w-2 h-2 rounded-full ${channel.active ? 'bg-primary' : 'bg-outline'}`} />
          <span className="text-[10px] font-bold text-white uppercase tracking-tighter">
            {channel.active ? 'Monitoring' : 'Paused'}
          </span>
        </div>
      </div>

      {/* Info */}
      <div className="flex-1 min-w-0">
        <div className="flex items-center gap-3 mb-1">
          <a
            href={`https://www.twitch.tv/${channel.channelName}`}
            target="_blank"
            rel="noreferrer"
            className="text-lg font-bold text-on-surface tracking-tight hover:text-primary transition-colors uppercase"
          >
            {channel.channelName}
          </a>
          {channel.active && (
            <span className="px-2 py-0.5 bg-primary/10 text-primary text-[10px] font-black rounded uppercase">
              Active
            </span>
          )}
        </div>
        <p className={`text-xs mb-2 flex items-center gap-1 ${queueStateTone(channel)}`}>
          <span className="material-symbols-outlined text-xs">hourglass_top</span>
          {queueStateLabel(channel)}
        </p>
        <div className="grid grid-cols-3 gap-3 mb-3">
          <div className="rounded-lg bg-surface/60 border border-white/5 px-3 py-2">
            <p className="text-[10px] uppercase tracking-widest text-on-surface-variant/70">Downloaded</p>
            <p className="mt-1 text-lg font-bold text-on-surface">{channel.totalVodsDownloaded}</p>
          </div>
          <div className="rounded-lg bg-surface/60 border border-white/5 px-3 py-2">
            <p className="text-[10px] uppercase tracking-widest text-on-surface-variant/70">Uploaded</p>
            <p className="mt-1 text-lg font-bold text-on-surface">{channel.totalVodsUploaded}</p>
          </div>
          <div className="rounded-lg bg-surface/60 border border-white/5 px-3 py-2">
            <p className="text-[10px] uppercase tracking-widest text-on-surface-variant/70">Last Upload</p>
            <p className="mt-1 text-sm font-semibold text-on-surface leading-tight">{formatRelativeTime(channel.lastUploadedAtUTC)}</p>
          </div>
        </div>
        {accountName && (
          <p className="text-xs text-primary/80 mb-2 flex items-center gap-1">
            <span className="material-symbols-outlined text-xs">smart_display</span>
            Uploads to: {accountName}
          </p>
        )}
        {!accountName && channel.youTubeAccountId == null && (
          <p className="text-xs text-on-surface-variant/50 mb-2 flex items-center gap-1">
            <span className="material-symbols-outlined text-xs">link_off</span>
            No YouTube account assigned
          </p>
        )}
        <div className="flex items-center gap-4">
          <button
            onClick={onQueueNext}
            disabled={!channel.active || channel.currentVodId !== null}
            className="px-2.5 py-1 rounded-md text-xs font-bold text-primary hover:bg-primary/10 disabled:text-on-surface-variant/30 disabled:hover:bg-transparent transition-colors"
          >
            Fetch next VOD
          </button>
          <button
            onClick={onEdit}
            className="p-1.5 text-on-surface-variant hover:text-on-surface hover:bg-white/5 rounded transition-colors"
          >
            <span className="material-symbols-outlined text-lg">edit</span>
          </button>
          <button
            onClick={onDelete}
            className="p-1.5 text-on-surface-variant hover:text-error hover:bg-error/5 rounded transition-colors"
          >
            <span className="material-symbols-outlined text-lg">delete</span>
          </button>
        </div>
      </div>

      {/* Toggle + Menu */}
      <div className="flex items-center gap-8 px-6 flex-shrink-0">
        <div className="flex flex-col items-end">
          <span className="text-[10px] text-outline uppercase font-bold tracking-widest mb-2">Channel State</span>
          <label className="relative inline-flex items-center cursor-pointer">
            <input
              type="checkbox"
              className="sr-only peer"
              checked={channel.active}
              onChange={onToggle}
            />
            <div className="w-11 h-6 bg-surface-container-highest rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:start-[2px] after:bg-white after:border-gray-300 after:border after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-primary" />
          </label>
        </div>
      </div>
    </div>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────

export default function ChannelsPage() {
  const [channels, setChannels] = useState<ChannelQueueStatus[]>([]);
  const [accounts, setAccounts] = useState<YouTubeAccount[]>([]);
  const [avatarUrls, setAvatarUrls] = useState<Record<string, string>>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [dialog, setDialog] = useState<Partial<Channel> | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<ChannelQueueStatus | null>(null);

  async function load() {
    try {
      setError(null);
      const data = await channelsApi.getAll();
      setChannels(data);
      channelsApi.getAvatarUrls().then(setAvatarUrls).catch(() => {});
      accountsApi.getAll().then(setAccounts).catch(() => {});
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { load(); }, []);

  async function handleSave(c: Partial<Channel>) {
    if (c.id) {
      await channelsApi.update(c as Channel);
    } else {
      await channelsApi.create({ channelName: c.channelName!, active: c.active ?? true, youTubeAccountId: c.youTubeAccountId ?? null });
    }
    setDialog(null);
    await load();
  }

  async function handleToggle(channel: ChannelQueueStatus) {
    const { id, channelName, addedAtUTC, youTubeAccountId } = channel;
    await channelsApi.update({ id, channelName, addedAtUTC, youTubeAccountId, active: !channel.active });
    await load();
  }

  async function handleDelete(channel: ChannelQueueStatus) {
    await channelsApi.delete(channel.id);
    setConfirmDelete(null);
    await load();
  }

  async function handleQueueNext(channel: ChannelQueueStatus) {
    await channelsApi.queueNext(channel.id);
    await load();
  }

  const activeCount = channels.filter(c => c.active).length;

  return (
    <>
      {/* Header */}
      <div className="flex items-end justify-between mb-10">
        <div>
          <h1 className="text-3xl font-black tracking-tight text-on-surface mb-2">Channels</h1>
          <p className="text-on-surface-variant max-w-md">
            Choose which Twitch channels to monitor and where each completed VOD should upload.
          </p>
        </div>
        <div className="flex gap-3">
          <button
            onClick={() => setDialog({ active: true })}
            className="px-4 py-2 bg-gradient-to-br from-primary to-primary-container text-on-primary-container text-sm font-bold rounded-lg hover:brightness-110 transition-all flex items-center gap-2 shadow-lg shadow-primary/10"
          >
            <span className="material-symbols-outlined text-sm" style={{ fontVariationSettings: "'FILL' 1" }}>add</span>
            Add Channel
          </button>
        </div>
      </div>

      {/* Error */}
      {error && (
        <div className="mb-6 p-4 bg-error-container/10 border border-error/20 rounded-xl text-error flex items-center gap-3">
          <span className="material-symbols-outlined">error</span>
          <span className="text-sm">{error}</span>
          <button onClick={load} className="ml-auto text-xs font-bold underline">Retry</button>
        </div>
      )}

      {/* Loading */}
      {loading && (
        <div className="flex items-center justify-center py-20">
          <div className="w-8 h-8 border-2 border-primary/30 border-t-primary rounded-full animate-spin" />
        </div>
      )}

      {/* Channel Rows */}
      {!loading && (
        <div className="flex flex-col gap-4">
          {channels.map(c => (
            <ChannelRow
              key={c.id}
              channel={c}
              avatarUrl={avatarUrls[c.channelName.toLowerCase()]}
              accountName={accounts.find(a => a.id === c.youTubeAccountId)?.name}
              onEdit={() => setDialog({ ...c })}
              onToggle={() => handleToggle(c)}
              onQueueNext={() => handleQueueNext(c)}
              onDelete={() => setConfirmDelete(c)}
            />
          ))}
        </div>
      )}

      {/* Empty state */}
      {!loading && channels.length === 0 && (
        <div className="flex flex-col items-center justify-center py-24 gap-4">
          <span className="material-symbols-outlined text-5xl text-on-surface-variant/30">grid_view</span>
          <p className="text-on-surface-variant text-sm">
            No channels yet. Click <strong className="text-on-surface">Add Channel</strong> to get started.
          </p>
        </div>
      )}

      {/* Bento Stats */}
      {!loading && channels.length > 0 && (
        <div className="grid grid-cols-4 gap-4 mt-8">
          <div className="bg-surface-container-low p-6 rounded-xl border border-white/5">
            <p className="text-[10px] text-outline uppercase font-black tracking-widest mb-1">Total Active</p>
            <p className="text-3xl font-black text-primary">{activeCount}</p>
          </div>
          <div className="bg-surface-container-low p-6 rounded-xl border border-white/5">
            <p className="text-[10px] text-outline uppercase font-black tracking-widest mb-1">Total Channels</p>
            <p className="text-3xl font-black text-on-surface">{channels.length}</p>
          </div>
          <div className="bg-surface-container-low p-6 rounded-xl border border-white/5">
            <p className="text-[10px] text-outline uppercase font-black tracking-widest mb-1">Paused</p>
            <p className="text-3xl font-black text-on-surface">{channels.length - activeCount}</p>
          </div>
          <div className="bg-surface-container-low p-6 rounded-xl border border-white/5">
            <p className="text-[10px] text-outline uppercase font-black tracking-widest mb-1">Ready For Next VOD</p>
            <div className="flex items-center gap-2 mt-2">
              <span className="text-3xl font-black text-on-surface">{channels.filter(c => c.active && !c.currentVodId).length}</span>
              <span className="text-xs font-bold text-on-surface-variant/70">channels</span>
            </div>
          </div>
        </div>
      )}

      {/* Add / Edit dialog */}
      {dialog !== null && (
        <ChannelDialog
          channel={dialog}
          accounts={accounts}
          onSave={handleSave}
          onClose={() => setDialog(null)}
        />
      )}

      {/* Delete confirmation */}
      {confirmDelete && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm">
          <div className="bg-surface-container rounded-xl w-full max-w-sm p-6 shadow-[0px_10px_30px_rgba(6,14,32,0.5)] border border-outline-variant/[0.15]">
            <h2 className="text-lg font-bold mb-2">Delete Channel</h2>
            <p className="text-sm text-on-surface-variant mb-6">
              Are you sure you want to delete <strong className="text-on-surface">{confirmDelete.channelName}</strong>?
              Existing pipeline jobs will not be removed.
            </p>
            <div className="flex justify-end gap-3">
              <button
                onClick={() => setConfirmDelete(null)}
                className="px-5 py-2 rounded-lg text-sm font-semibold text-on-surface-variant hover:bg-surface-bright transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={() => handleDelete(confirmDelete)}
                className="px-5 py-2 rounded-lg text-sm font-bold bg-error text-on-error hover:brightness-110 transition-all"
              >
                Delete
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
