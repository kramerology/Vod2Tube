export default function SettingsPage() {
  const placeholderSections = [
    { icon: 'key', title: 'API Keys', desc: 'Configure Twitch and YouTube API credentials.' },
    { icon: 'folder', title: 'Storage Paths', desc: 'Set download, render, and output directories.' },
    { icon: 'tune', title: 'Pipeline Defaults', desc: 'Default quality, retry limits, and scheduling options.' },
    { icon: 'notifications', title: 'Notifications', desc: 'Discord webhooks, email alerts, and event triggers.' },
    { icon: 'palette', title: 'Appearance', desc: 'Theme customization and UI preferences.' },
  ];

  return (
    <>
      {/* Header */}
      <div className="mb-10">
        <h1 className="text-3xl font-bold tracking-tight text-on-surface mb-1">Settings</h1>
        <p className="text-on-surface-variant text-sm">Configuration panel · Coming soon</p>
      </div>

      {/* Under Construction Banner */}
      <div className="bg-surface-container-low rounded-xl border border-white/5 p-6 mb-8">
        <div className="flex items-center gap-4">
          <div className="w-14 h-14 rounded-lg bg-primary/10 flex items-center justify-center flex-shrink-0">
            <span className="material-symbols-outlined text-3xl text-primary">construction</span>
          </div>
          <div>
            <h2 className="font-bold text-on-surface text-lg">Under Construction</h2>
            <p className="text-on-surface-variant text-sm mt-1">
              Settings functionality is not yet implemented in the backend.
              The sections below are placeholders for future development.
            </p>
          </div>
        </div>
      </div>

      {/* Placeholder Cards */}
      <div className="bg-surface-container-low rounded-xl border border-white/5 overflow-hidden divide-y divide-white/5">
        {placeholderSections.map(s => (
          <div
            key={s.title}
            className="p-5 flex items-center gap-4 opacity-50 cursor-not-allowed hover:bg-white/[0.02] transition-colors"
          >
            <div className="w-10 h-10 rounded-lg bg-surface-container-highest flex items-center justify-center flex-shrink-0">
              <span className="material-symbols-outlined text-xl text-on-surface-variant">{s.icon}</span>
            </div>
            <div className="flex-1">
              <h3 className="font-bold text-sm text-on-surface">{s.title}</h3>
              <p className="text-xs text-on-surface-variant mt-0.5">{s.desc}</p>
            </div>
            <span className="material-symbols-outlined text-on-surface-variant/30">lock</span>
          </div>
        ))}
      </div>
    </>
  );
}
