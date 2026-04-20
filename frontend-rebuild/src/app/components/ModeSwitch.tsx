interface ModeSwitchProps {
  value: 'manual' | 'auto';
  onChange: (value: 'manual' | 'auto') => void;
  theme?: 'light' | 'dark';
}

export function ModeSwitch({ value, onChange, theme = 'light' }: ModeSwitchProps) {
  const isDark = theme === 'dark';

  return (
    <div className="space-y-2">
      <div className="flex items-center gap-2">
        <span className={`text-[11px] ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>Provider 路由模式</span>
        <span className={`px-1.5 py-0.5 text-[9px] rounded ${
          value === 'manual'
            ? (isDark ? 'bg-white/15 text-white/80' : 'bg-[#f3f3f3] text-[#605e5c]')
            : (isDark ? 'bg-[#0078d4]/30 text-[#60cdff]' : 'bg-[#0067c0]/10 text-[#0067c0]')
        }`}>
          {value === 'manual' ? '手动' : '自动'}
        </span>
      </div>

      <div className={`inline-flex rounded p-0.5 ${
        isDark ? 'bg-white/10' : 'bg-black/5'
      }`}>
        <button
          onClick={() => onChange('manual')}
          className={`px-3 h-7 text-[12px] rounded transition-all ${
            value === 'manual'
              ? (isDark ? 'bg-[#2d2d2d] text-white shadow-sm' : 'bg-white text-[#1c1c1c] shadow-sm')
              : (isDark ? 'text-white/60 hover:text-white/80' : 'text-[#605e5c] hover:text-[#1c1c1c]')
          }`}
        >
          手动切换
        </button>
        <button
          onClick={() => onChange('auto')}
          className={`px-3 h-7 text-[12px] rounded transition-all ${
            value === 'auto'
              ? (isDark ? 'bg-[#2d2d2d] text-white shadow-sm' : 'bg-white text-[#1c1c1c] shadow-sm')
              : (isDark ? 'text-white/60 hover:text-white/80' : 'text-[#605e5c] hover:text-[#1c1c1c]')
          }`}
        >
          自动切换
        </button>
      </div>

      <div className={`text-[10px] leading-relaxed ${isDark ? 'text-white/50' : 'text-[#605e5c]'}`}>
        {value === 'manual'
          ? '始终使用当前手动选中的 Provider / 账号'
          : '自动根据状态、额度信息与本地使用量选择更合适的 Provider / 账号'
        }
      </div>
    </div>
  );
}
