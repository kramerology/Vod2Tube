import { NavLink } from 'react-router-dom';

interface NavItem {
  to: string;
  icon: string;
  label: string;
}

const NAV: NavItem[] = [
  { to: '/',     icon: 'tv',               label: 'Channels'  },
  { to: '/vods', icon: 'queue_play_next',   label: 'VOD Queue' },
];

function SidebarLink({ to, icon, label }: NavItem) {
  return (
    <NavLink
      to={to}
      end
      className={({ isActive }) =>
        `flex items-center gap-3 px-4 py-3 rounded-lg text-sm font-medium transition-all ${
          isActive
            ? 'bg-[#3B82F6]/10 text-[#3B82F6] border-l-4 border-[#3B82F6]'
            : 'text-on-surface/70 hover:text-on-surface hover:bg-surface-variant border-l-4 border-transparent'
        }`
      }
    >
      <span className="material-symbols-outlined">{icon}</span>
      <span>{label}</span>
    </NavLink>
  );
}

export default function Layout({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-screen bg-surface-dim text-on-surface">
      {/* Top app bar */}
      <header className="fixed top-0 w-full z-50 bg-[#0b1326]/80 backdrop-blur-xl shadow-[0px_12px_32px_rgba(218,226,253,0.06)] flex items-center justify-between px-6 h-16">
        <div className="flex items-center gap-8">
          <span className="text-xl font-bold tracking-tighter text-on-surface font-headline">
            VOD2TUBE
          </span>
          <nav className="hidden md:flex items-center gap-1">
            {NAV.map(n => (
              <NavLink
                key={n.to}
                to={n.to}
                end
                className={({ isActive }) =>
                  `text-sm px-3 py-1 rounded transition-colors ${
                    isActive ? 'text-[#3B82F6] font-bold' : 'text-on-surface/60 hover:text-on-surface'
                  }`
                }
              >
                {n.label}
              </NavLink>
            ))}
          </nav>
        </div>
        <div className="flex items-center gap-2">
          <div className="flex items-center bg-surface-container rounded-lg px-3 py-1.5 gap-2 border border-outline-variant/20">
            <span className="material-symbols-outlined text-sm text-on-surface/60">search</span>
            <input
              className="bg-transparent border-none focus:outline-none text-sm text-on-surface placeholder:text-on-surface/30 w-40"
              placeholder="Search..."
              type="text"
            />
          </div>
          <button className="p-2 text-on-surface/60 hover:bg-surface-bright transition-colors rounded-lg">
            <span className="material-symbols-outlined">settings</span>
          </button>
        </div>
      </header>

      {/* Sidebar */}
      <aside className="hidden md:flex fixed left-0 top-16 h-[calc(100vh-64px)] w-64 border-r border-outline-variant/20 bg-surface-container flex-col py-8 px-4 z-40">
        <div className="mb-8 px-2">
          <div className="font-headline font-bold text-on-surface text-lg">VOD Processor</div>
          <div className="text-on-surface/50 text-xs tracking-widest uppercase mt-1">Precision Laboratory</div>
        </div>
        <nav className="flex-1 space-y-1">
          {NAV.map(n => <SidebarLink key={n.to} {...n} />)}
        </nav>
      </aside>

      {/* Page content */}
      <main className="md:ml-64 pt-24 pb-12 px-6 lg:px-12 max-w-7xl mx-auto">
        {children}
      </main>
    </div>
  );
}
