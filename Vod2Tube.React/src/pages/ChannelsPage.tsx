import { useEffect, useState } from 'react';
import { channelsApi, type Channel } from '../api/client';

// ── Add / Edit dialog ─────────────────────────────────────────────────────────

function ChannelDialog({
  channel,
  onSave,
  onClose,
}: {
  channel: Partial<Channel>;
  onSave: (c: Partial<Channel>) => Promise<void>;
  onClose: () => void;
}) {
  const [name, setName] = useState(channel.channelName ?? '');
  const [active, setActive] = useState(channel.active ?? true);
  const [saving, setSaving] = useState(false);

  async function handleSave() {
    if (!name.trim()) return;
    setSaving(true);
    await onSave({ ...channel, channelName: name.trim(), active });
    setSaving(false);
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm">
      <div className="bg-surface-container rounded-2xl w-full max-w-md p-6 shadow-2xl border border-outline-variant/20">
        <h2 className="text-lg font-bold font-headline mb-6 flex items-center gap-2">
          <span className="material-symbols-outlined text-primary">{channel.id ? 'edit' : 'person_add'}</span>
          {channel.id ? 'Edit Channel' : 'Add Channel'}
        </h2>

        <label className="block mb-1 text-xs uppercase tracking-widest text-on-surface-variant font-bold">
          Twitch Username
        </label>
        <div className="relative mb-5">
          <span className="absolute inset-y-0 left-3 flex items-center text-primary/60 material-symbols-outlined text-lg">
            tv
          </span>
          <input
            className="w-full bg-surface-dim border border-outline-variant/20 rounded-xl py-3 pl-10 pr-4 text-on-surface focus:outline-none focus:ring-2 focus:ring-primary/30 focus:border-primary transition-all"
            placeholder="e.g. pirate_software"
            value={name}
            onChange={e => setName(e.target.value)}
            onKeyDown={e => e.key === 'Enter' && handleSave()}
          />
        </div>

        <label className="flex items-center gap-3 cursor-pointer mb-6 select-none">
          <button
            type="button"
            onClick={() => setActive(v => !v)}
            className={`relative w-12 h-6 rounded-full transition-colors ${active ? 'bg-primary' : 'bg-surface-variant'}`}
          >
            <span
              className={`absolute top-1 left-1 w-4 h-4 rounded-full bg-on-primary transition-transform ${active ? 'translate-x-6' : ''}`}
            />
          </button>
          <span className="text-sm text-on-surface-variant">
            {active ? 'Active — monitor for new VODs' : 'Paused'}
          </span>
        </label>

        <div className="flex justify-end gap-3">
          <button
            onClick={onClose}
            className="px-5 py-2 rounded-xl text-sm font-bold text-on-surface-variant hover:bg-surface-bright transition-colors"
          >
            Cancel
          </button>
          <button
            onClick={handleSave}
            disabled={!name.trim() || saving}
            className="px-6 py-2 rounded-xl text-sm font-bold bg-primary text-on-primary hover:brightness-110 disabled:opacity-40 transition-all"
          >
            {saving ? 'Saving…' : 'Save'}
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Channel Card ──────────────────────────────────────────────────────────────

function ChannelCard({
  channel,
  avatarUrl,
  onEdit,
  onToggle,
  onDelete,
}: {
  channel: Channel;
  avatarUrl?: string;
  onEdit: () => void;
  onToggle: () => void;
  onDelete: () => void;
}) {
  const added = new Date(channel.addedAtUTC).toLocaleDateString('en-US', {
    month: 'short', day: 'numeric', year: 'numeric',
  });

  return (
    <div
      className={`group bg-surface-container rounded-3xl overflow-hidden hover:ring-2 transition-all duration-300 flex flex-col ${
        channel.active
          ? 'ring-primary/0 hover:ring-primary/30'
          : 'grayscale-[0.5] hover:grayscale-0 hover:ring-outline-variant/30'
      }`}
    >
      {/* Banner */}
      <div className="relative h-36 bg-surface-container-highest overflow-hidden">
        {avatarUrl
          ? <img src={avatarUrl} alt={channel.channelName} className="absolute inset-0 w-full h-full object-cover" />
          : <div
              className="absolute inset-0"
              style={{
                background: channel.active
                  ? 'linear-gradient(135deg, #3B82F6 0%, #4edea3 100%)'
                  : 'linear-gradient(135deg, #424754 0%, #2d3449 100%)',
              }}
            />
        }
        {/* Darken overlay so the badge is always readable */}
        <div className="absolute inset-0 bg-black/30" />
        {/* Status badge */}
        <div className="absolute top-3 right-3">
          {channel.active ? (
            <span className="flex items-center gap-1.5 bg-tertiary/15 text-tertiary px-3 py-1 rounded-full text-[10px] font-black uppercase tracking-tighter border border-tertiary/20 backdrop-blur-md">
              <span className="w-1.5 h-1.5 bg-tertiary rounded-full animate-pulse" />
              Active
            </span>
          ) : (
            <span className="flex items-center gap-1.5 bg-outline-variant/20 text-on-surface-variant px-3 py-1 rounded-full text-[10px] font-black uppercase tracking-tighter border border-outline-variant/20 backdrop-blur-md">
              <span className="w-1.5 h-1.5 bg-outline-variant rounded-full" />
              Paused
            </span>
          )}
        </div>
      </div>

      {/* Body */}
      <div className="px-5 pt-5 pb-5 flex-1 flex flex-col">
        <div className="mb-3">
          <a
            href={`https://www.twitch.tv/${channel.channelName}`}
            target="_blank"
            rel="noreferrer"
            className="text-lg font-bold tracking-tight text-on-surface hover:text-primary transition-colors"
          >
            {channel.channelName}
          </a>
          <p className="text-xs text-on-surface-variant mt-0.5">Added {added}</p>
        </div>

        <div className="mt-auto grid grid-cols-3 gap-2">
          <button
            onClick={onToggle}
            className={`flex items-center justify-center gap-1.5 py-2 rounded-xl text-xs font-bold transition-all border ${
              channel.active
                ? 'bg-surface-variant hover:bg-error-container/20 hover:text-error border-outline-variant/10'
                : 'bg-primary/10 text-primary hover:bg-primary hover:text-on-primary border-primary/20'
            }`}
          >
            <span className="material-symbols-outlined text-sm">
              {channel.active ? 'pause_circle' : 'play_arrow'}
            </span>
            {channel.active ? 'Pause' : 'Activate'}
          </button>
          <button
            onClick={onEdit}
            className="flex items-center justify-center gap-1.5 py-2 rounded-xl bg-surface-variant hover:bg-surface-bright text-xs font-bold transition-all border border-outline-variant/10"
          >
            <span className="material-symbols-outlined text-sm">edit</span>
            Edit
          </button>
          <button
            onClick={onDelete}
            className="flex items-center justify-center gap-1.5 py-2 rounded-xl bg-surface-variant hover:bg-error-container/20 hover:text-error text-xs font-bold transition-all border border-outline-variant/10"
          >
            <span className="material-symbols-outlined text-sm">delete</span>
            Delete
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────

export default function ChannelsPage() {
  const [channels, setChannels] = useState<Channel[]>([]);
  const [avatarUrls, setAvatarUrls] = useState<Record<string, string>>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [dialog, setDialog] = useState<Partial<Channel> | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<Channel | null>(null);

  async function load() {
    try {
      setError(null);
      const data = await channelsApi.getAll();
      setChannels(data);
      channelsApi.getAvatarUrls().then(setAvatarUrls).catch(() => {});
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
      await channelsApi.create({ channelName: c.channelName!, active: c.active ?? true });
    }
    setDialog(null);
    await load();
  }

  async function handleToggle(channel: Channel) {
    await channelsApi.update({ ...channel, active: !channel.active });
    await load();
  }

  async function handleDelete(channel: Channel) {
    await channelsApi.delete(channel.id);
    setConfirmDelete(null);
    await load();
  }

  return (
    <>
      {/* Header */}
      <div className="flex flex-col md:flex-row md:items-end justify-between gap-6 mb-10">
        <div>
          <h1 className="text-4xl font-bold tracking-tighter text-on-surface">Channels</h1>
          <p className="text-on-surface-variant mt-2 max-w-xl">
            Manage automated VOD ingestion pipelines for tracked Twitch broadcasters.
          </p>
        </div>
      </div>

      {/* Add channel bar */}
      <section className="bg-surface-container-low rounded-2xl p-5 mb-8 border border-outline-variant/10 shadow-lg">
        <form
          className="flex flex-col sm:flex-row gap-3"
          onSubmit={e => {
            e.preventDefault();
            setDialog({ active: true });
          }}
        >
          <div className="flex-1 relative group">
            <span className="absolute inset-y-0 left-4 flex items-center pointer-events-none text-primary/50 group-focus-within:text-primary transition-colors material-symbols-outlined">
              person_add
            </span>
            <input
              readOnly
              onClick={() => setDialog({ active: true })}
              className="w-full cursor-pointer bg-surface-dim border border-outline-variant/20 rounded-xl py-3.5 pl-12 pr-4 text-on-surface focus:ring-2 focus:ring-primary/20 focus:border-primary outline-none"
              placeholder="Click to add a Twitch channel…"
            />
          </div>
          <button
            type="button"
            onClick={() => setDialog({ active: true })}
            className="bg-primary text-on-primary px-7 py-3.5 rounded-xl font-bold flex items-center justify-center gap-2 hover:brightness-110 active:scale-95 transition-all"
          >
            <span className="material-symbols-outlined">add</span>
            Add Channel
          </button>
        </form>
      </section>

      {/* Error */}
      {error && (
        <div className="mb-6 p-4 bg-error-container/20 border border-error/30 rounded-xl text-error flex items-center gap-3">
          <span className="material-symbols-outlined">error</span>
          <span>{error}</span>
          <button onClick={load} className="ml-auto text-xs font-bold underline">Retry</button>
        </div>
      )}

      {/* Loading */}
      {loading && (
        <div className="flex items-center justify-center py-20">
          <div className="w-8 h-8 border-2 border-primary/30 border-t-primary rounded-full animate-spin" />
        </div>
      )}

      {/* Grid */}
      {!loading && (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-5">
          {channels.map(c => (
            <ChannelCard
              key={c.id}
              channel={c}
              avatarUrl={avatarUrls[c.channelName.toLowerCase()]}
              onEdit={() => setDialog({ ...c })}
              onToggle={() => handleToggle(c)}
              onDelete={() => setConfirmDelete(c)}
            />
          ))}

          {/* Empty state / add placeholder */}
          <button
            onClick={() => setDialog({ active: true })}
            className="group bg-surface-container-low border-2 border-dashed border-outline-variant/20 rounded-3xl flex flex-col items-center justify-center p-10 hover:border-primary/40 transition-all cursor-pointer"
          >
            <div className="w-14 h-14 rounded-full bg-surface-container-highest flex items-center justify-center mb-3 group-hover:scale-110 transition-transform">
              <span className="material-symbols-outlined text-2xl text-primary-fixed-dim">add</span>
            </div>
            <h4 className="font-bold text-on-surface text-sm">Track New Channel</h4>
            <p className="text-xs text-on-surface-variant text-center mt-1">Add a username to start automated indexing.</p>
          </button>
        </div>
      )}

      {/* No channels */}
      {!loading && channels.length === 0 && (
        <p className="text-center text-on-surface-variant py-12">
          No channels yet. Click <strong>Add Channel</strong> to get started.
        </p>
      )}

      {/* Add / Edit dialog */}
      {dialog !== null && (
        <ChannelDialog
          channel={dialog}
          onSave={handleSave}
          onClose={() => setDialog(null)}
        />
      )}

      {/* Delete confirmation */}
      {confirmDelete && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm">
          <div className="bg-surface-container rounded-2xl w-full max-w-sm p-6 shadow-2xl border border-outline-variant/20">
            <h2 className="text-lg font-bold font-headline mb-2">Delete Channel</h2>
            <p className="text-sm text-on-surface-variant mb-6">
              Are you sure you want to delete <strong className="text-on-surface">{confirmDelete.channelName}</strong>?
              Existing pipeline jobs will not be removed.
            </p>
            <div className="flex justify-end gap-3">
              <button
                onClick={() => setConfirmDelete(null)}
                className="px-5 py-2 rounded-xl text-sm font-bold text-on-surface-variant hover:bg-surface-bright transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={() => handleDelete(confirmDelete)}
                className="px-5 py-2 rounded-xl text-sm font-bold bg-error text-on-error hover:brightness-110 transition-all"
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
