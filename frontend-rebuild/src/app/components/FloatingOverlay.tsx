interface FloatingOverlayProps {
  expanded?: boolean;
  accountType?: 'openai' | 'compatible';
}

export function FloatingOverlay({ expanded = false, accountType = 'openai' }: FloatingOverlayProps) {
  return (
    <div
      className="rounded-lg overflow-hidden select-none"
      style={{
        width: expanded ? '320px' : '280px',
        backgroundColor: 'rgba(32, 32, 32, 0.88)',
        backdropFilter: 'blur(30px)',
        border: '1px solid rgba(255, 255, 255, 0.08)',
        boxShadow: '0 8px 32px rgba(0, 0, 0, 0.4), 0 2px 8px rgba(0, 0, 0, 0.3)'
      }}
    >
      {/* Header */}
      <div className="px-3 py-2 flex items-center justify-between border-b border-white/5">
        <div className="flex items-center gap-2">
          <div className="w-1.5 h-1.5 rounded-full bg-[#0078d4]"></div>
          <span className="text-[10px] text-white/60">CodexBar 状态</span>
        </div>
        <div className="flex items-center gap-2">
          {/* Pin Icon */}
          <button className="w-4 h-4 flex items-center justify-center hover:bg-white/10 rounded transition-colors">
            <svg width="10" height="10" viewBox="0 0 10 10" className="text-white/60">
              <path d="M5 1L5 6M3 4L7 4L6 8L5 9L4 8L3 4Z" stroke="currentColor" strokeWidth="0.8" fill="none"/>
            </svg>
          </button>
          {/* Opacity Icon */}
          <button className="w-4 h-4 flex items-center justify-center hover:bg-white/10 rounded transition-colors">
            <svg width="10" height="10" viewBox="0 0 10 10" className="text-white/60">
              <circle cx="5" cy="5" r="3" stroke="currentColor" strokeWidth="0.8" fill="none"/>
              <path d="M2 5Q5 2 8 5" stroke="currentColor" strokeWidth="0.8" fill="none"/>
            </svg>
          </button>
          {/* Close Icon */}
          <button className="w-4 h-4 flex items-center justify-center hover:bg-[#c42b1c] rounded transition-colors group">
            <svg width="8" height="8" viewBox="0 0 8 8">
              <path d="M1 1L7 7M7 1L1 7" stroke="currentColor" strokeWidth="1" className="text-white/60 group-hover:text-white"/>
            </svg>
          </button>
        </div>
      </div>

      {/* Content */}
      <div className="p-3">
        {/* Model Name - Primary */}
        <div className="mb-2">
          <div className="flex items-center gap-2">
            <span className="text-[16px] font-medium text-white">GPT-4.5</span>
            <span className="px-1.5 py-0.5 text-[9px] bg-[#0078d4] text-white rounded">
              {accountType === 'openai' ? 'OpenAI' : '兼容 Provider'}
            </span>
            {accountType === 'openai' && (
              <span className="px-1.5 py-0.5 text-[9px] bg-[#8764b8]/80 text-white rounded">聚合网关</span>
            )}
          </div>
          {expanded && (
            <div className="text-[10px] text-white/50 mt-1">推理强度: medium</div>
          )}
        </div>

        {/* Provider & Account */}
        <div className="mb-3 space-y-1">
          <div className="flex items-center justify-between text-[11px]">
            <span className="text-white/50">Provider</span>
            <span className="text-white/90">
              {accountType === 'openai' ? 'OpenAI' : 'DeepSeek API'}
            </span>
          </div>
          <div className="flex items-center justify-between text-[11px]">
            <span className="text-white/50">账号</span>
            <span className="text-white/90 truncate ml-2">
              {accountType === 'openai' ? 'work@company.com' : 'test_account'}
            </span>
          </div>
        </div>

        {/* Usage Stats */}
        {accountType === 'openai' ? (
          <>
            {/* OpenAI Quota */}
            <div className="space-y-2">
              <div className="flex items-center justify-between text-[10px]">
                <span className="text-white/50">5 小时剩余</span>
                <span className="text-[#6fce6f]">67.2%</span>
              </div>
              <div className="h-1 bg-white/10 rounded-full overflow-hidden">
                <div className="h-full bg-[#6fce6f] rounded-full" style={{ width: '32.8%' }}></div>
              </div>

              {expanded && (
                <>
                  <div className="flex items-center justify-between text-[10px]">
                    <span className="text-white/50">本周剩余</span>
                    <span className="text-[#faa21b]">54.7%</span>
                  </div>
                  <div className="h-1 bg-white/10 rounded-full overflow-hidden">
                    <div className="h-full bg-[#faa21b] rounded-full" style={{ width: '45.3%' }}></div>
                  </div>

                  <div className="pt-2 mt-2 border-t border-white/5 grid grid-cols-3 gap-2 text-[10px]">
                    <div className="text-center">
                      <div className="text-white/40 mb-1">今日</div>
                      <div className="text-white/90">12.5K</div>
                    </div>
                    <div className="text-center">
                      <div className="text-white/40 mb-1">近 7 天</div>
                      <div className="text-white/90">89.3K</div>
                    </div>
                    <div className="text-center">
                      <div className="text-white/40 mb-1">近 30 天</div>
                      <div className="text-white/90">342K</div>
                    </div>
                  </div>
                </>
              )}
            </div>
          </>
        ) : (
          <>
            {/* Third-party API token stats */}
            <div className="space-y-2">
              <div className="flex items-center justify-between text-[10px] mb-2">
                <span className="text-white/50">本地统计</span>
                <span className="px-1.5 py-0.5 bg-white/10 text-white/70 rounded text-[9px]">非官方计费</span>
              </div>
              <div className="grid grid-cols-3 gap-2 text-[10px]">
                <div className="text-center">
                  <div className="text-white/40 mb-1">今日</div>
                  <div className="text-white/90">12.5K</div>
                </div>
                <div className="text-center">
                  <div className="text-white/40 mb-1">近 7 天</div>
                  <div className="text-white/90">89.3K</div>
                </div>
                <div className="text-center">
                  <div className="text-white/40 mb-1">近 30 天</div>
                  <div className="text-white/90">342K</div>
                </div>
              </div>

              {expanded && (
                <div className="pt-2 mt-2 border-t border-white/5">
                  <div className="flex items-center justify-between text-[10px]">
                    <span className="text-white/50">连接状态</span>
                    <div className="flex items-center gap-1.5">
                      <div className="w-1.5 h-1.5 rounded-full bg-[#6fce6f]"></div>
                      <span className="text-[#6fce6f]">在线</span>
                    </div>
                  </div>
                  <div className="flex items-center justify-between text-[10px] mt-1.5">
                    <span className="text-white/50">Base URL</span>
                    <span className="text-white/70 text-[9px]">api.deepseek.com</span>
                  </div>
                </div>
              )}
            </div>
          </>
        )}

        {/* Footer */}
        <div className="flex items-center justify-between mt-3 pt-2 border-t border-white/5">
          <div className="text-[9px] text-white/40">2 分钟前刷新</div>
          <div className="flex items-center gap-2">
            <button className="w-4 h-4 flex items-center justify-center hover:bg-white/10 rounded transition-colors">
              <svg width="9" height="9" viewBox="0 0 9 9" className="text-white/50">
                <path d="M4.5 1.5A3 3 0 1 1 1.5 4.5M1.5 1.5v3h3" stroke="currentColor" strokeWidth="0.8" fill="none"/>
              </svg>
            </button>
            {expanded && (
              <button className="text-[9px] text-white/50 hover:text-white/80 transition-colors">收起</button>
            )}
            {!expanded && (
              <button className="text-[9px] text-white/50 hover:text-white/80 transition-colors">展开</button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
