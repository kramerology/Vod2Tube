import { NavLink } from 'react-router-dom';

interface NavItem {
  to: string;
  icon: string;
  label: string;
}

const NAV: NavItem[] = [
  { to: '/channels', icon: 'grid_view',        label: 'Channels'  },
  { to: '/vods',     icon: 'queue_play_next',   label: 'VOD Queue' },
  { to: '/settings', icon: 'settings',          label: 'Settings'  },
];

export default function Layout({ children }: { children: React.ReactNode }) {
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
          {children}
        </main>
      </div>
    </div>
  );
}
