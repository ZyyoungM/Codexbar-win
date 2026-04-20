interface IconButtonProps {
  icon: 'launch' | 'detect' | 'edit' | 'delete' | 'refresh' | 'pin' | 'opacity' | 'close' | 'expand' | 'collapse';
  tooltip?: string;
  onClick?: () => void;
  theme?: 'light' | 'dark';
  size?: 'sm' | 'md';
}

export function IconButton({ icon, tooltip, onClick, theme = 'light', size = 'sm' }: IconButtonProps) {
  const isDark = theme === 'dark';
  const sizeClass = size === 'sm' ? 'w-6 h-6' : 'w-7 h-7';

  const icons = {
    launch: (
      <svg width="12" height="12" viewBox="0 0 12 12">
        <path d="M3 2L3 10L9 6L3 2Z" fill="currentColor"/>
      </svg>
    ),
    detect: (
      <svg width="12" height="12" viewBox="0 0 12 12">
        <circle cx="6" cy="6" r="2" fill="currentColor"/>
        <circle cx="6" cy="6" r="4" stroke="currentColor" strokeWidth="0.8" fill="none"/>
        <path d="M6 1v1M6 10v1M1 6h1M10 6h1" stroke="currentColor" strokeWidth="0.8"/>
      </svg>
    ),
    edit: (
      <svg width="12" height="12" viewBox="0 0 12 12">
        <path d="M8 1l3 3-6 6H2v-3l6-6z" stroke="currentColor" strokeWidth="0.8" fill="none"/>
        <path d="M7 2l3 3" stroke="currentColor" strokeWidth="0.8"/>
      </svg>
    ),
    delete: (
      <svg width="12" height="12" viewBox="0 0 12 12">
        <path d="M2 3h8M4 3V2h4v1M3 3v7a1 1 0 001 1h4a1 1 0 001-1V3" stroke="currentColor" strokeWidth="0.8" fill="none"/>
        <path d="M5 5v3M7 5v3" stroke="currentColor" strokeWidth="0.8"/>
      </svg>
    ),
    refresh: (
      <svg width="11" height="11" viewBox="0 0 11 11">
        <path d="M5.5 1.5A4 4 0 1 1 1.5 5.5M1.5 1.5v4h4" stroke="currentColor" strokeWidth="1" fill="none"/>
      </svg>
    ),
    pin: (
      <svg width="11" height="11" viewBox="0 0 11 11">
        <path d="M5.5 1.5L5.5 7M3.5 5L7.5 5L6.5 9L5.5 10L4.5 9L3.5 5Z" stroke="currentColor" strokeWidth="1" fill="none"/>
      </svg>
    ),
    opacity: (
      <svg width="11" height="11" viewBox="0 0 11 11">
        <circle cx="5.5" cy="5.5" r="3.5" stroke="currentColor" strokeWidth="1" fill="none"/>
        <path d="M2 5.5Q5.5 2 9 5.5" stroke="currentColor" strokeWidth="1" fill="none"/>
      </svg>
    ),
    close: (
      <svg width="10" height="10" viewBox="0 0 10 10">
        <path d="M1 1L9 9M9 1L1 9" stroke="currentColor" strokeWidth="1.2"/>
      </svg>
    ),
    expand: (
      <svg width="10" height="10" viewBox="0 0 10 10">
        <path d="M2 4L5 7L8 4" stroke="currentColor" strokeWidth="1" fill="none"/>
      </svg>
    ),
    collapse: (
      <svg width="10" height="10" viewBox="0 0 10 10">
        <path d="M2 6L5 3L8 6" stroke="currentColor" strokeWidth="1" fill="none"/>
      </svg>
    )
  };

  return (
    <button
      className={`${sizeClass} inline-flex items-center justify-center rounded transition-all cursor-default group relative ${
        isDark
          ? 'text-white/70 hover:bg-white/10 hover:text-white active:bg-white/15'
          : 'text-[#605e5c] hover:bg-black/5 hover:text-[#1c1c1c] active:bg-black/10'
      }`}
      onClick={onClick}
      title={tooltip}
    >
      {icons[icon]}
      {tooltip && (
        <span className={`absolute bottom-full mb-1 px-2 py-1 text-[10px] rounded whitespace-nowrap opacity-0 group-hover:opacity-100 transition-opacity pointer-events-none ${
          isDark ? 'bg-[#2d2d2d] text-white' : 'bg-[#f9f9f9] text-[#1c1c1c] border border-black/10'
        }`}>
          {tooltip}
        </span>
      )}
    </button>
  );
}
