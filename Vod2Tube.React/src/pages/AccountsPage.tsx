import { useEffect, useState, useCallback } from 'react';
import { accountsApi, type YouTubeAccount } from '../api/client';

// ── Step-by-step setup wizard ─────────────────────────────────────────────────

function SetupWizard({
  onSave,
  onClose,
}: {
  onSave: (name: string, json: string) => Promise<void>;
  onClose: () => void;
}) {
  const [step, setStep] = useState(1);
  const [name, setName] = useState('');
  const [json, setJson] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const totalSteps = 4;

  async function handleSave() {
    if (!name.trim() || !json.trim()) return;
    setSaving(true);
    setError('');
    try {
      await onSave(name.trim(), json.trim());
    } catch (e) {
      setError((e as Error).message);
      setSaving(false);
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm">
      <div className="bg-surface-container rounded-xl w-full max-w-2xl p-6 shadow-[0px_10px_30px_rgba(6,14,32,0.5)] border border-outline-variant/[0.15] max-h-[90vh] overflow-y-auto">
        {/* Header */}
        <div className="flex items-center justify-between mb-6">
          <h2 className="text-lg font-bold flex items-center gap-2">
            <span className="material-symbols-outlined text-primary">add_circle</span>
            Add YouTube Account
          </h2>
          <div className="flex items-center gap-2 text-xs text-on-surface-variant">
            {Array.from({ length: totalSteps }, (_, i) => (
              <div
                key={i}
                className={`w-2 h-2 rounded-full transition-colors ${
                  i + 1 <= step ? 'bg-primary' : 'bg-surface-container-highest'
                }`}
              />
            ))}
            <span className="ml-1">Step {step}/{totalSteps}</span>
          </div>
        </div>

        {/* Step 1: Name */}
        {step === 1 && (
          <div className="space-y-4">
            <div className="bg-surface-container-low rounded-lg p-4 border border-white/5">
              <h3 className="text-sm font-bold text-on-surface mb-2 flex items-center gap-2">
                <span className="material-symbols-outlined text-primary text-lg">badge</span>
                Give this account a name
              </h3>
              <p className="text-xs text-on-surface-variant mb-4">
                Choose a friendly name so you can easily identify this YouTube account later.
                For example: "Gaming Channel" or "Highlights Account".
              </p>
              <input
                className="w-full bg-surface-container-highest border border-outline-variant/20 rounded-lg py-3 px-4 text-on-surface focus:outline-none focus:ring-1 focus:ring-primary/50 transition-all placeholder:text-on-surface-variant/50"
                placeholder='e.g. "My Gaming Channel"'
                value={name}
                onChange={e => setName(e.target.value)}
                onKeyDown={e => e.key === 'Enter' && name.trim() && setStep(2)}
                autoFocus
              />
            </div>
          </div>
        )}

        {/* Step 2: Google Cloud Console instructions */}
        {step === 2 && (
          <div className="space-y-4">
            <div className="bg-surface-container-low rounded-lg p-4 border border-white/5">
              <h3 className="text-sm font-bold text-on-surface mb-3 flex items-center gap-2">
                <span className="material-symbols-outlined text-primary text-lg">cloud</span>
                Set up Google Cloud credentials
              </h3>
              <p className="text-xs text-on-surface-variant mb-4">
                Follow these steps in the Google Cloud Console. Don't worry — it's free and only takes a few minutes!
              </p>

              <ol className="space-y-3 text-sm text-on-surface-variant">
                <li className="flex gap-3">
                  <span className="flex-shrink-0 w-6 h-6 rounded-full bg-primary/10 text-primary text-xs font-bold flex items-center justify-center">1</span>
                  <div>
                    <span className="text-on-surface font-medium">Go to Google Cloud Console</span>
                    <br />
                    <a href="https://console.cloud.google.com/" target="_blank" rel="noreferrer"
                      className="text-primary hover:underline text-xs">
                      console.cloud.google.com ↗
                    </a>
                  </div>
                </li>
                <li className="flex gap-3">
                  <span className="flex-shrink-0 w-6 h-6 rounded-full bg-primary/10 text-primary text-xs font-bold flex items-center justify-center">2</span>
                  <div>
                    <span className="text-on-surface font-medium">Create a new project</span> (or select an existing one)
                  </div>
                </li>
                <li className="flex gap-3">
                  <span className="flex-shrink-0 w-6 h-6 rounded-full bg-primary/10 text-primary text-xs font-bold flex items-center justify-center">3</span>
                  <div>
                    <span className="text-on-surface font-medium">Enable the YouTube Data API v3</span>
                    <br />
                    <span className="text-xs">Go to "APIs & Services" → "Library" → search "YouTube Data API v3" → Enable</span>
                  </div>
                </li>
                <li className="flex gap-3">
                  <span className="flex-shrink-0 w-6 h-6 rounded-full bg-primary/10 text-primary text-xs font-bold flex items-center justify-center">4</span>
                  <div>
                    <span className="text-on-surface font-medium">Configure the OAuth consent screen</span>
                    <br />
                    <span className="text-xs">Go to "APIs & Services" → "OAuth consent screen" → choose "External" → fill in the app name</span>
                  </div>
                </li>
                <li className="flex gap-3">
                  <span className="flex-shrink-0 w-6 h-6 rounded-full bg-primary/10 text-primary text-xs font-bold flex items-center justify-center">5</span>
                  <div>
                    <span className="text-on-surface font-medium">Create OAuth 2.0 credentials</span>
                    <br />
                    <span className="text-xs">
                      Go to "APIs & Services" → "Credentials" → "Create Credentials" → "OAuth client ID" → choose <strong className="text-on-surface">"Desktop app"</strong>
                    </span>
                  </div>
                </li>
                <li className="flex gap-3">
                  <span className="flex-shrink-0 w-6 h-6 rounded-full bg-primary/10 text-primary text-xs font-bold flex items-center justify-center">6</span>
                  <div>
                    <span className="text-on-surface font-medium">Download the JSON file</span>
                    <br />
                    <span className="text-xs">Click the download button (⬇️) next to your new credentials</span>
                  </div>
                </li>
              </ol>
            </div>

            <div className="bg-tertiary/5 border border-tertiary/20 rounded-lg p-3 flex items-start gap-2">
              <span className="material-symbols-outlined text-tertiary text-lg flex-shrink-0 mt-0.5">lightbulb</span>
              <p className="text-xs text-on-surface-variant">
                <strong className="text-tertiary">Tip:</strong> If you've already set up credentials before, you can reuse the same Google Cloud project.
                Just create a new OAuth client ID for each YouTube account.
              </p>
            </div>
          </div>
        )}

        {/* Step 3: Paste JSON */}
        {step === 3 && (
          <div className="space-y-4">
            <div className="bg-surface-container-low rounded-lg p-4 border border-white/5">
              <h3 className="text-sm font-bold text-on-surface mb-2 flex items-center gap-2">
                <span className="material-symbols-outlined text-primary text-lg">data_object</span>
                Paste your credentials
              </h3>
              <p className="text-xs text-on-surface-variant mb-4">
                Open the downloaded JSON file in any text editor, select all the content, copy it, and paste it below.
                The file is usually named something like <code className="bg-surface-container-highest px-1 py-0.5 rounded text-[11px]">client_secret_xxxxx.json</code>.
              </p>
              <textarea
                className="w-full bg-surface-container-highest border border-outline-variant/20 rounded-lg py-3 px-4 text-on-surface focus:outline-none focus:ring-1 focus:ring-primary/50 transition-all placeholder:text-on-surface-variant/50 font-mono text-xs h-40 resize-none"
                placeholder='{"installed":{"client_id":"...","client_secret":"...","redirect_uris":["..."],...}}'
                value={json}
                onChange={e => setJson(e.target.value)}
              />
              {json && !json.includes('"installed"') && !json.includes('"web"') && (
                <p className="mt-2 text-xs text-error flex items-center gap-1">
                  <span className="material-symbols-outlined text-sm">warning</span>
                  This doesn't look like a valid Google OAuth credentials file.
                  It should contain an "installed" or "web" key.
                </p>
              )}
            </div>
          </div>
        )}

        {/* Step 4: Confirm */}
        {step === 4 && (
          <div className="space-y-4">
            <div className="bg-surface-container-low rounded-lg p-4 border border-white/5">
              <h3 className="text-sm font-bold text-on-surface mb-3 flex items-center gap-2">
                <span className="material-symbols-outlined text-primary text-lg">check_circle</span>
                Ready to create
              </h3>
              <div className="space-y-3">
                <div className="flex items-center gap-3">
                  <span className="text-[10px] uppercase tracking-widest text-on-surface-variant font-bold w-24">Name</span>
                  <span className="text-sm text-on-surface font-medium">{name}</span>
                </div>
                <div className="flex items-center gap-3">
                  <span className="text-[10px] uppercase tracking-widest text-on-surface-variant font-bold w-24">Credentials</span>
                  <span className="text-sm text-emerald-400 flex items-center gap-1">
                    <span className="material-symbols-outlined text-sm">check</span>
                    Provided
                  </span>
                </div>
              </div>
              <p className="text-xs text-on-surface-variant mt-4">
                After creating the account, you'll need to authorize it by signing in with your Google account.
                This will open a new browser tab.
              </p>
            </div>

            {error && (
              <div className="bg-error-container/10 border border-error/20 rounded-lg p-3 flex items-center gap-2">
                <span className="material-symbols-outlined text-error text-lg">error</span>
                <span className="text-xs text-error">{error}</span>
              </div>
            )}
          </div>
        )}

        {/* Navigation */}
        <div className="flex justify-between mt-6">
          <button
            onClick={step === 1 ? onClose : () => setStep(s => s - 1)}
            className="px-5 py-2 rounded-lg text-sm font-semibold text-on-surface-variant hover:bg-surface-bright transition-colors"
          >
            {step === 1 ? 'Cancel' : 'Back'}
          </button>
          <div className="flex gap-3">
            {step < totalSteps ? (
              <button
                onClick={() => setStep(s => s + 1)}
                disabled={
                  (step === 1 && !name.trim()) ||
                  (step === 3 && !json.trim())
                }
                className="px-6 py-2 rounded-lg text-sm font-bold bg-gradient-to-br from-primary to-primary-container text-on-primary-container hover:opacity-90 disabled:opacity-40 transition-all shadow-lg shadow-primary/10"
              >
                Continue
              </button>
            ) : (
              <button
                onClick={handleSave}
                disabled={saving}
                className="px-6 py-2 rounded-lg text-sm font-bold bg-gradient-to-br from-primary to-primary-container text-on-primary-container hover:opacity-90 disabled:opacity-40 transition-all shadow-lg shadow-primary/10"
              >
                {saving ? 'Creating…' : 'Create Account'}
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

// ── Rename dialog ─────────────────────────────────────────────────────────────

function RenameDialog({
  account,
  onSave,
  onClose,
}: {
  account: YouTubeAccount;
  onSave: (id: number, name: string) => Promise<void>;
  onClose: () => void;
}) {
  const [name, setName] = useState(account.name);
  const [saving, setSaving] = useState(false);

  async function handleSave() {
    if (!name.trim()) return;
    setSaving(true);
    await onSave(account.id, name.trim());
    setSaving(false);
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm">
      <div className="bg-surface-container rounded-xl w-full max-w-md p-6 shadow-[0px_10px_30px_rgba(6,14,32,0.5)] border border-outline-variant/[0.15]">
        <h2 className="text-lg font-bold mb-6 flex items-center gap-2">
          <span className="material-symbols-outlined text-primary">edit</span>
          Rename Account
        </h2>

        <label className="block mb-1 text-[10px] uppercase tracking-widest text-on-surface-variant font-bold">
          Account Name
        </label>
        <input
          className="w-full bg-surface-container-highest border border-outline-variant/20 rounded-lg py-3 px-4 text-on-surface focus:outline-none focus:ring-1 focus:ring-primary/50 transition-all placeholder:text-on-surface-variant/50 mb-6"
          value={name}
          onChange={e => setName(e.target.value)}
          onKeyDown={e => e.key === 'Enter' && handleSave()}
          autoFocus
        />

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

// ── Account Row Card ──────────────────────────────────────────────────────────

function AccountRow({
  account,
  onRename,
  onAuthorize,
  onRevoke,
  onDelete,
}: {
  account: YouTubeAccount;
  onRename: () => void;
  onAuthorize: () => void;
  onRevoke: () => void;
  onDelete: () => void;
}) {
  const added = new Date(account.addedAtUTC).toLocaleDateString('en-US', {
    month: 'short', day: 'numeric', year: 'numeric',
  });

  return (
    <div
      className={`bg-surface-container-low group hover:bg-surface-container transition-all duration-300 rounded-xl p-4 flex items-center gap-6 ${
        account.isAuthorized ? 'border-l-2 border-emerald-500' : 'border-l-2 border-tertiary'
      }`}
    >
      {/* Icon area */}
      <div className="relative w-48 h-24 rounded-lg overflow-hidden flex-shrink-0">
        <div
          className="w-full h-full flex items-center justify-center"
          style={{ background: 'linear-gradient(135deg, #131b2e 0%, #222a3d 100%)' }}
        >
          <span
            className={`material-symbols-outlined text-4xl transition-colors ${
              account.isAuthorized ? 'text-emerald-400' : 'text-tertiary/50'
            }`}
            style={{ fontVariationSettings: "'FILL' 1" }}
          >
            {account.isAuthorized ? 'smart_display' : 'link_off'}
          </span>
        </div>
        <div className="absolute inset-0 bg-gradient-to-t from-black/80 to-transparent" />
        <div className="absolute bottom-2 left-2 flex items-center gap-1.5">
          <span className={`w-2 h-2 rounded-full ${account.isAuthorized ? 'bg-emerald-500' : 'bg-tertiary'}`} />
          <span className="text-[10px] font-bold text-white uppercase tracking-tighter">
            {account.isAuthorized ? 'Connected' : 'Not Linked'}
          </span>
        </div>
      </div>

      {/* Info */}
      <div className="flex-1 min-w-0">
        <div className="flex items-center gap-3 mb-1">
          <span className="text-lg font-bold text-on-surface tracking-tight">
            {account.name}
          </span>
          {account.isAuthorized && (
            <span className="px-2 py-0.5 bg-emerald-500/10 text-emerald-400 text-[10px] font-black rounded uppercase">
              Authorized
            </span>
          )}
          {!account.isAuthorized && (
            <span className="px-2 py-0.5 bg-tertiary/10 text-tertiary text-[10px] font-black rounded uppercase">
              Needs Auth
            </span>
          )}
        </div>
        {account.channelTitle && (
          <p className="text-sm text-primary mb-1 flex items-center gap-1">
            <span className="material-symbols-outlined text-sm">smart_display</span>
            {account.channelTitle}
          </p>
        )}
        <p className="text-sm text-on-surface-variant mb-3">
          YouTube upload account. Added {added}.
        </p>
        <div className="flex items-center gap-4">
          <button
            onClick={onRename}
            className="p-1.5 text-on-surface-variant hover:text-on-surface hover:bg-white/5 rounded transition-colors"
            title="Rename"
          >
            <span className="material-symbols-outlined text-lg">edit</span>
          </button>
          <button
            onClick={onDelete}
            className="p-1.5 text-on-surface-variant hover:text-error hover:bg-error/5 rounded transition-colors"
            title="Delete"
          >
            <span className="material-symbols-outlined text-lg">delete</span>
          </button>
        </div>
      </div>

      {/* Auth controls */}
      <div className="flex items-center gap-4 px-6 flex-shrink-0">
        {account.isAuthorized ? (
          <button
            onClick={onRevoke}
            className="px-4 py-2 rounded-lg text-sm font-semibold text-on-surface-variant border border-outline-variant/20 hover:bg-surface-bright hover:text-error transition-colors"
          >
            Revoke Access
          </button>
        ) : (
          <button
            onClick={onAuthorize}
            className="px-5 py-2 rounded-lg text-sm font-bold bg-gradient-to-br from-primary to-primary-container text-on-primary-container hover:brightness-110 transition-all shadow-lg shadow-primary/10 flex items-center gap-2"
          >
            <span className="material-symbols-outlined text-sm">link</span>
            Authorize
          </button>
        )}
      </div>
    </div>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────

export default function AccountsPage() {
  const [accounts, setAccounts] = useState<YouTubeAccount[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showWizard, setShowWizard] = useState(false);
  const [renameTarget, setRenameTarget] = useState<YouTubeAccount | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<YouTubeAccount | null>(null);

  const load = useCallback(async () => {
    try {
      setError(null);
      const data = await accountsApi.getAll();
      setAccounts(data);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  // Listen for OAuth completion from popup window
  useEffect(() => {
    function handleMessage(event: MessageEvent) {
      if (event.data?.type === 'vod2tube-oauth-complete') {
        // Refresh the list to pick up the new auth status
        load();
      }
    }
    window.addEventListener('message', handleMessage);
    return () => window.removeEventListener('message', handleMessage);
  }, [load]);

  async function handleCreate(name: string, json: string) {
    await accountsApi.create(name, json);
    setShowWizard(false);
    await load();
  }

  async function handleRename(id: number, name: string) {
    await accountsApi.update(id, name);
    setRenameTarget(null);
    await load();
  }

  async function handleAuthorize(account: YouTubeAccount) {
    try {
      const { authorizationUrl } = await accountsApi.authorize(account.id);
      window.open(authorizationUrl, '_blank', 'noopener');
    } catch (e) {
      setError((e as Error).message);
    }
  }

  async function handleRevoke(account: YouTubeAccount) {
    await accountsApi.revoke(account.id);
    await load();
  }

  async function handleDelete(account: YouTubeAccount) {
    await accountsApi.delete(account.id);
    setConfirmDelete(null);
    await load();
  }

  const authorizedCount = accounts.filter(a => a.isAuthorized).length;

  return (
    <>
      {/* Header */}
      <div className="flex items-end justify-between mb-10">
        <div>
          <h1 className="text-3xl font-black tracking-tight text-on-surface mb-2">YouTube Accounts</h1>
          <p className="text-on-surface-variant max-w-md">
            Manage YouTube accounts for uploading VODs. Each channel can upload to a different account.
          </p>
        </div>
        <div className="flex gap-3">
          <button
            onClick={() => setShowWizard(true)}
            className="px-4 py-2 bg-gradient-to-br from-primary to-primary-container text-on-primary-container text-sm font-bold rounded-lg hover:brightness-110 transition-all flex items-center gap-2 shadow-lg shadow-primary/10"
          >
            <span className="material-symbols-outlined text-sm" style={{ fontVariationSettings: "'FILL' 1" }}>add</span>
            Add Account
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

      {/* Account Rows */}
      {!loading && (
        <div className="flex flex-col gap-4">
          {accounts.map(a => (
            <AccountRow
              key={a.id}
              account={a}
              onRename={() => setRenameTarget(a)}
              onAuthorize={() => handleAuthorize(a)}
              onRevoke={() => handleRevoke(a)}
              onDelete={() => setConfirmDelete(a)}
            />
          ))}
        </div>
      )}

      {/* Empty state */}
      {!loading && accounts.length === 0 && (
        <div className="flex flex-col items-center justify-center py-24 gap-4">
          <span className="material-symbols-outlined text-5xl text-on-surface-variant/30">smart_display</span>
          <p className="text-on-surface-variant text-sm">
            No YouTube accounts yet. Click <strong className="text-on-surface">Add Account</strong> to connect one.
          </p>
        </div>
      )}

      {/* Bento Stats */}
      {!loading && accounts.length > 0 && (
        <div className="grid grid-cols-3 gap-4 mt-8">
          <div className="bg-surface-container-low p-6 rounded-xl border border-white/5">
            <p className="text-[10px] text-outline uppercase font-black tracking-widest mb-1">Authorized</p>
            <p className="text-3xl font-black text-emerald-400">{authorizedCount}</p>
          </div>
          <div className="bg-surface-container-low p-6 rounded-xl border border-white/5">
            <p className="text-[10px] text-outline uppercase font-black tracking-widest mb-1">Total Accounts</p>
            <p className="text-3xl font-black text-on-surface">{accounts.length}</p>
          </div>
          <div className="bg-surface-container-low p-6 rounded-xl border border-white/5">
            <p className="text-[10px] text-outline uppercase font-black tracking-widest mb-1">Pending Auth</p>
            <p className="text-3xl font-black text-tertiary">{accounts.length - authorizedCount}</p>
          </div>
        </div>
      )}

      {/* Setup wizard */}
      {showWizard && (
        <SetupWizard
          onSave={handleCreate}
          onClose={() => setShowWizard(false)}
        />
      )}

      {/* Rename dialog */}
      {renameTarget && (
        <RenameDialog
          account={renameTarget}
          onSave={handleRename}
          onClose={() => setRenameTarget(null)}
        />
      )}

      {/* Delete confirmation */}
      {confirmDelete && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm">
          <div className="bg-surface-container rounded-xl w-full max-w-sm p-6 shadow-[0px_10px_30px_rgba(6,14,32,0.5)] border border-outline-variant/[0.15]">
            <h2 className="text-lg font-bold mb-2">Delete Account</h2>
            <p className="text-sm text-on-surface-variant mb-2">
              Are you sure you want to delete <strong className="text-on-surface">{confirmDelete.name}</strong>?
            </p>
            <p className="text-xs text-on-surface-variant mb-6">
              Any channels using this account will no longer have an upload destination.
              Stored credentials and tokens will be removed.
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
