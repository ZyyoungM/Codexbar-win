interface Windows11WindowProps {
  title: string;
  width: number;
  height: number;
  children: React.ReactNode;
  className?: string;
  theme?: 'light' | 'dark';
}

export function Windows11Window({ title, width, height, children, className = '', theme = 'light' }: Windows11WindowProps) {
  const isDark = theme === 'dark';

  return (
    <div
      className={`rounded-lg overflow-hidden shadow-2xl ${className}`}
      style={{
        width: `${width}px`,
        height: `${height}px`,
        backgroundColor: isDark ? 'rgba(32, 32, 32, 0.9)' : 'rgba(243, 243, 243, 0.9)',
        backdropFilter: 'blur(40px)',
        border: isDark ? '1px solid rgba(255, 255, 255, 0.08)' : '1px solid rgba(0, 0, 0, 0.08)'
      }}
    >
      {/* Title bar */}
      <div
        className="h-8 flex items-center justify-between px-3 select-none"
        style={{
          backgroundColor: isDark ? 'rgba(32, 32, 32, 0.6)' : 'rgba(243, 243, 243, 0.6)',
          borderBottom: isDark ? '1px solid rgba(255, 255, 255, 0.06)' : '1px solid rgba(0, 0, 0, 0.06)'
        }}
      >
        <span className={`text-[12px] font-normal ${isDark ? 'text-white/90' : 'text-[#1c1c1c]'}`}>{title}</span>
        <div className="flex items-center gap-3">
          <div className={`w-3 h-3 rounded flex items-center justify-center cursor-default ${isDark ? 'hover:bg-white/10' : 'hover:bg-black/5'}`}>
            <svg width="10" height="1" viewBox="0 0 10 1">
              <rect width="10" height="1" fill={isDark ? '#fff' : '#1c1c1c'}/>
            </svg>
          </div>
          <div className={`w-3 h-3 rounded flex items-center justify-center cursor-default ${isDark ? 'hover:bg-white/10' : 'hover:bg-black/5'}`}>
            <svg width="10" height="10" viewBox="0 0 10 10">
              <rect x="0" y="0" width="10" height="10" stroke={isDark ? '#fff' : '#1c1c1c'} strokeWidth="1" fill="none"/>
            </svg>
          </div>
          <div className="w-3 h-3 hover:bg-[#c42b1c] hover:text-white rounded flex items-center justify-center cursor-default group">
            <svg width="10" height="10" viewBox="0 0 10 10">
              <path d="M0,0 L10,10 M10,0 L0,10" stroke="currentColor" strokeWidth="1" className={`${isDark ? 'text-white' : 'text-[#1c1c1c]'} group-hover:text-white`}/>
            </svg>
          </div>
        </div>
      </div>

      {/* Content */}
      <div className="h-[calc(100%-2rem)] overflow-hidden">
        {children}
      </div>
    </div>
  );
}
