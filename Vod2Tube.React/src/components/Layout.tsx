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
  { to: '/vods',     icon: 'queue_play_next',   label: 'Vods'      },
  { to: '/accounts', icon: 'smart_display',     label: 'YouTube'   },
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
      <div className="flex">
        {/* ── Side Nav Bar ──────────────────────────────────────── */}
        <aside className="hidden md:flex bg-gradient-to-b from-[#131b2e] to-[#0b1326] border-r border-white/5 h-screen w-64 fixed left-0 top-0 pt-6 flex-col text-sm font-medium z-40">
          <div className="px-6 pb-5">
            <span className="text-xl font-black tracking-tighter text-slate-100">Vod2Tube</span>
            <p className="mt-1 text-[11px] text-slate-400">Twitch VOD downloads, rendering, and uploads</p>
          </div>
          <nav className="flex flex-col gap-1 px-3">
            {NAV.map(n => (
              <NavLink
                key={n.to}
                to={n.to}
                className={({ isActive }) =>
                  isActive
                    ? 'text-blue-400 bg-blue-500/10 border border-blue-500/30 rounded-lg flex items-center px-4 py-3 cursor-pointer'
                    : 'text-slate-400 hover:text-slate-100 hover:bg-white/5 transition-all flex items-center px-4 py-3 cursor-pointer group border border-transparent rounded-lg'
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
                    {readiness.message} VOD processing will stay paused until these paths point to valid executables.
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
