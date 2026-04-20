import { IconButton } from './IconButton';

interface CompactFloatingOverlayProps {
  expanded?: boolean;
  accountType?: 'openai' | 'compatible';
  theme?: 'light' | 'dark';
}

export function CompactFloatingOverlay({ expanded = false, accountType = 'openai', theme = 'dark' }: CompactFloatingOverlayProps) {
  const isDark = theme === 'dark';

  return (
    <div
      className="rounded overflow-hidden select-none cursor-move"
      style={{
        width: expanded ? '260px' : '230px',
        backgroundColor: isDark ? 'rgba(32, 32, 32, 0.92)' : 'rgba(243, 243, 243, 0.92)',
        backdropFilter: 'blur(30px)',
        border: isDark ? '0.5px solid rgba(255, 255, 255, 0.06)' : '0.5px solid rgba(0, 0, 0, 0.06)',
        boxShadow: isDark
          ? '0 8px 32px rgba(0, 0, 0, 0.5), 0 2px 8px rgba(0, 0, 0, 0.35)'
          : '0 8px 24px rgba(0, 0, 0, 0.18), 0 2px 6px rgba(0, 0, 0, 0.1)'
      }}
    >
      {/* Content - No separate header */}
      <div className="p-2">
        {/* Row 1: Brand + Model + Provider + Controls */}
        <div className="flex items-center justify-between mb-1.5">
          <div className="flex items-center gap-1.5 flex-1 min-w-0">
            {/* Tiny brand indicator */}
            <div className="flex items-center gap-1 flex-shrink-0">
              <div className={`w-1 h-1 rounded-full ${isDark ? 'bg-[#60cdff]' : 'bg-[#0067c0]'}`}></div>
              <span className={`text-[7px] ${isDark ? 'text-white/40' : 'text-[#605e5c]'}`}>CodexBar</span>
            </div>
            
            {/* Model name - strongest element */}
            <span className={`text-[13px] font-medium truncate ${isDark ? 'text-white' : 'text-[#1c1c1c]'}`}>GPT-4.5</span>
            
            {/* Provider badge */}
            <span className={`px-1 py-0.5 text-[7px] rounded flex-shrink-0 ${
              accountType === 'openai'
                ? (isDark ? 'bg-white/12 text-white/70' : 'bg-[#f3f3f3] text-[#605e5c]')
                : (isDark ? 'bg-[#8764b8]/50 text-white/90' : 'bg-[#8764b8] text-white')
            }`}>
              {accountType === 'openai' ? 'OpenAI' : '兼容'}
            </span>
          </div>

          {/* Top-right icon controls */}
          <div className="flex items-center gap-0.5 flex-shrink-0">
            <IconButton icon="pin" theme={theme} size="sm" />
            <IconButton icon="opacity" theme={theme} size="sm" />
            <IconButton icon="close" theme={theme} size="sm" />
          </div>
        </div>

        {/* Row 2: Main metric */}
        {accountType === 'openai' ? (
          <div className="mb-1.5">
            <div className="flex items-center justify-between text-[9px] mb-0.5">
              <span className={isDark ? 'text-white/50' : 'text-[#605e5c]'}>5h 额度</span>
              <span className="text-[#6fce6f]">67.2%</span>
            </div>
            <div className={`h-0.5 rounded-full overflow-hidden ${isDark ? 'bg-white/8' : 'bg-black/8'}`}>
              <div className="h-full bg-[#6fce6f] rounded-full transition-all" style={{ width: '32.8%' }}></div>
            </div>
          </div>
        ) : (
          <div className="flex items-center justify-between text-[9px] mb-1.5">
            <span className={isDark ? 'text-white/50' : 'text-[#605e5c]'}>今日</span>
            <span className={isDark ? 'text-white/90' : 'text-[#1c1c1c]'}>12.5K tokens</span>
          </div>
        )}

        {/* Expanded details */}
        {expanded && (
          <>
            {accountType === 'openai' ? (
              <div className="space-y-1.5 mb-1.5">
                <div className="flex items-center justify-between text-[9px]">
                  <span className={isDark ? 'text-white/50' : 'text-[#605e5c]'}>周额度</span>
                  <span className="text-[#faa21b]">54.7%</span>
                </div>
                <div className={`h-0.5 rounded-full overflow-hidden ${isDark ? 'bg-white/8' : 'bg-black/8'}`}>
                  <div className="h-full bg-[#faa21b] rounded-full transition-all" style={{ width: '45.3%' }}></div>
                </div>

                <div className={`pt-1.5 mt-1 border-t ${isDark ? 'border-white/5' : 'border-black/5'} grid grid-cols-3 gap-1 text-[8px]`}>
                  <div className="text-center">
                    <div className={isDark ? 'text-white/35' : 'text-[#605e5c]'}>今日</div>
                    <div className={`mt-0.5 ${isDark ? 'text-white/85' : 'text-[#1c1c1c]'}`}>12.5K</div>
                  </div>
                  <div className="text-center">
                    <div className={isDark ? 'text-white/35' : 'text-[#605e5c]'}>本周</div>
                    <div className={`mt-0.5 ${isDark ? 'text-white/85' : 'text-[#1c1c1c]'}`}>89K</div>
                  </div>
                  <div className="text-center">
                    <div className={isDark ? 'text-white/35' : 'text-[#605e5c]'}>本月</div>
                    <div className={`mt-0.5 ${isDark ? 'text-white/85' : 'text-[#1c1c1c]'}`}>342K</div>
                  </div>
                </div>
              </div>
            ) : (
              <div className={`mb-1.5 pb-1.5 border-b ${isDark ? 'border-white/5' : 'border-black/5'}`}>
                <div className="grid grid-cols-3 gap-1 text-[8px]">
                  <div className="text-center">
                    <div className={isDark ? 'text-white/35' : 'text-[#605e5c]'}>今日</div>
                    <div className={`mt-0.5 ${isDark ? 'text-white/85' : 'text-[#1c1c1c]'}`}>12.5K</div>
                  </div>
                  <div className="text-center">
                    <div className={isDark ? 'text-white/35' : 'text-[#605e5c]'}>本周</div>
                    <div className={`mt-0.5 ${isDark ? 'text-white/85' : 'text-[#1c1c1c]'}`}>89K</div>
                  </div>
                  <div className="text-center">
                    <div className={isDark ? 'text-white/35' : 'text-[#605e5c]'}>本月</div>
                    <div className={`mt-0.5 ${isDark ? 'text-white/85' : 'text-[#1c1c1c]'}`}>342K</div>
                  </div>
                </div>
              </div>
            )}

            {/* Account info in expanded state */}
            <div className="space-y-0.5 mb-1.5 text-[8px]">
              <div className="flex items-center justify-between">
                <span className={isDark ? 'text-white/40' : 'text-[#605e5c]'}>账号</span>
                <span className={`truncate ml-2 ${isDark ? 'text-white/75' : 'text-[#1c1c1c]'}`}>
                  {accountType === 'openai' ? 'work@...' : 'test_acc'}
                </span>
              </div>
              <div className="flex items-center justify-between">
                <span className={isDark ? 'text-white/40' : 'text-[#605e5c]'}>推理强度</span>
                <span className={`truncate ml-2 ${isDark ? 'text-white/75' : 'text-[#1c1c1c]'}`}>medium</span>
              </div>
            </div>
          </>
        )}

        {/* Bottom row: refresh time + controls */}
        <div className="flex items-center justify-between">
          <div className={`text-[7px] ${isDark ? 'text-white/35' : 'text-[#605e5c]'}`}>2分钟前</div>
          <div className="flex items-center gap-0.5">
            <IconButton icon="refresh" theme={theme} size="sm" />
            <IconButton icon={expanded ? 'collapse' : 'expand'} tooltip={expanded ? '收起' : '展开'} theme={theme} size="sm" />
          </div>
        </div>
      </div>
    </div>
  );
}