import { useEffect, useState } from 'react';
import { NavLink } from 'react-router-dom';
import { settingsApi, type ExecutableReadinessStatus } from '../api/client';

interface NavItem {
  to: string;
  icon: string;
  label: string;
}

const NAV: NavItem[] = [
  { to: '/channels', icon: 'grid_view',        label: 'Channels'  },
  { to: '/vods',     icon: 'queue_play_next',   label: 'VOD Queue' },
  { to: '/accounts', icon: 'smart_display',     label: 'Accounts'  },
  { to: '/settings', icon: 'settings',          label: 'Settings'  },
];

export default function Layout({ children }: { children: React.ReactNode }) {
  const [readiness, setReadiness] = useState<ExecutableReadinessStatus | null>(null);

  useEffect(() => {
    let cancelled = false;
    let timer: ReturnType<typeof setTimeout> | undefined;

    const load = async () => {
      try {
        const status = await settingsApi.getExecutableStatus();
        if (!cancelled) {
          setReadiness(status);
        }
      } catch {
        if (!cancelled) {
          setReadiness(null);
        }
      }
    };

    const poll = async () => {
      await load();
      if (!cancelled) {
        timer = setTimeout(poll, 15000);
      }
    };

    poll();

    return () => {
      cancelled = true;
      if (timer !== undefined) clearTimeout(timer);
    };
  }, []);

  const missingExecutables = readiness?.requiredExecutables.filter(x => !x.exists) ?? [];

  return (
    <div className="min-h-screen bg-surface text-on-surface antialiased selection:bg-primary/30 selection:text-primary">
      {/* ── Top Nav Bar ──────────────────────────────────────────── */}
      <header className="bg-[#131b2e] backdrop-blur-xl shadow-xl shadow-black/20 flex items-center justify-between px-6 w-full h-16 sticky top-0 z-50 tracking-tight">
        <div className="flex items-center gap-8">
          <span className="text-xl font-black tracking-tighter text-slate-100">
            Obsidian VOD
          </span>
          <div className="hidden md:flex items-center gap-6">
            <nav className="flex items-center gap-6">
              {NAV.map(n => (
                <NavLink
                  key={n.to}
                  to={n.to}
                  className={({ isActive }) =>
                    `text-sm transition-colors duration-200 ${
                      isActive
                        ? 'text-blue-400 font-semibold'
                        : 'text-slate-400 hover:text-slate-200'
                    }`
                  }
                >
                  {n.label}
                </NavLink>
              ))}
            </nav>
          </div>
        </div>
        <div className="flex items-center gap-4">
          <div className="relative group">
            <span className="material-symbols-outlined absolute left-3 top-1/2 -translate-y-1/2 text-on-surface-variant text-sm">search</span>
            <input
              className="bg-surface-container-highest/50 border-none rounded-lg pl-10 pr-4 py-1.5 text-sm w-64 focus:ring-1 focus:ring-primary/50 focus:outline-none transition-all placeholder:text-on-surface-variant/50"
              placeholder="Search queue..."
              type="text"
            />
          </div>
          <button className="p-2 text-slate-400 hover:text-slate-200 hover:bg-white/5 transition-colors duration-200 rounded-full">
            <span className="material-symbols-outlined">notifications</span>
          </button>
          <button className="p-2 text-slate-400 hover:text-slate-200 hover:bg-white/5 transition-colors duration-200 rounded-full">
            <span className="material-symbols-outlined">help</span>
          </button>
          <div className="h-8 w-8 rounded-full bg-gradient-to-br from-primary to-primary-container flex items-center justify-center cursor-pointer active:scale-95 transition-transform">
            <span className="material-symbols-outlined text-on-primary-container text-xl" style={{ fontVariationSettings: "'FILL' 1" }}>account_circle</span>
          </div>
        </div>
      </header>

      <div className="flex">
        {/* ── Side Nav Bar ──────────────────────────────────────── */}
        <aside className="hidden md:flex bg-gradient-to-b from-[#131b2e] to-[#0b1326] border-r border-white/5 h-screen w-64 fixed left-0 top-0 pt-20 flex-col gap-1 text-sm font-medium z-40">
          <div className="px-6 mb-8">
            <h2 className="text-lg font-bold text-slate-100">Studio Manager</h2>
            <p className="text-[10px] uppercase tracking-widest text-on-surface-variant opacity-60">Production Environment</p>
          </div>
          <nav className="flex flex-col gap-1">
            {NAV.map(n => (
              <NavLink
                key={n.to}
                to={n.to}
                className={({ isActive }) =>
                  isActive
                    ? 'text-blue-400 bg-blue-500/10 border-l-2 border-blue-500 flex items-center px-6 py-3 cursor-pointer'
                    : 'text-slate-400 hover:text-slate-100 hover:bg-white/5 transition-all flex items-center px-6 py-3 cursor-pointer group border-l-2 border-transparent'
                }
              >
                <span
                  className="material-symbols-outlined mr-3"
                >
                  {n.icon}
                </span>
                <span>{n.label}</span>
              </NavLink>
            ))}
          </nav>
          <div className="mt-auto p-6">
            <div className="bg-surface-container-high rounded-xl p-4 border border-white/5">
              <div className="flex justify-between items-center mb-2">
                <span className="text-[10px] font-bold text-primary uppercase">System Status</span>
                <div className="flex items-center gap-1.5">
                  <div className="w-1.5 h-1.5 rounded-full bg-emerald-500 animate-pulse" />
                  <span className="text-[10px] text-on-surface-variant">Online</span>
                </div>
              </div>
              <div className="w-full bg-surface-container-highest h-1 rounded-full overflow-hidden">
                <div className="bg-primary h-full w-[88%]" />
              </div>
            </div>
          </div>
        </aside>

        {/* ── Main Content Canvas ──────────────────────────────── */}
        <main className="flex-1 md:ml-64 p-8 min-h-screen bg-surface">
          {readiness && !readiness.isReady && (
            <div className="mb-6 rounded-xl border border-amber-400/25 bg-amber-500/10 px-5 py-4 text-sm text-amber-100 shadow-[0_8px_30px_rgba(245,158,11,0.08)]">
              <div className="flex items-start gap-3">
                <span className="material-symbols-outlined mt-0.5 text-xl text-amber-300">warning</span>
                <div className="flex-1">
                  <h2 className="font-semibold text-amber-50">Required tools need attention</h2>
                  <p className="mt-1 text-amber-100/90">
                    {readiness.message} Automated jobs are temporarily paused until these paths point to valid executables.
                  </p>
                  <ul className="mt-3 space-y-1 text-amber-100/85">
                    {missingExecutables.map(executable => (
                      <li key={executable.settingName} className="flex flex-wrap items-baseline gap-2">
                        <span className="font-medium text-amber-50">{executable.displayName}</span>
                        <span className="font-mono text-xs break-all">{executable.path || 'Path not configured'}</span>
                      </li>
                    ))}
                  </ul>
                  <p className="mt-3 text-xs uppercase tracking-widest text-amber-200/70">
                    Rechecked automatically every 15 seconds
                  </p>
                </div>
              </div>
            </div>
          )}
          {children}
        </main>
      </div>
    </div>
  );
}
