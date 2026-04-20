import { useState } from 'react';
import { MainFlyout } from './components/MainFlyout';
import { CompactFloatingOverlay } from './components/CompactFloatingOverlay';
import { OAuthDialog } from './components/OAuthDialog';
import { AddProviderDialog } from './components/AddProviderDialog';
import { SettingsWindow } from './components/SettingsWindow';
import { EditAccountDialog } from './components/EditAccountDialog';
import { Windows11Button } from './components/Windows11Button';
import type { FlyoutAccount } from './components/MainFlyout';

export default function App() {
  const [theme, setTheme] = useState<'light' | 'dark'>('light');
  const [overlayExpanded, setOverlayExpanded] = useState(false);
  const [overlayAccountType, setOverlayAccountType] = useState<'openai' | 'compatible'>('openai');
  const [showOAuth, setShowOAuth] = useState(false);
  const [showAddProvider, setShowAddProvider] = useState(false);
  const [showSettings, setShowSettings] = useState(false);
  const [editingAccount, setEditingAccount] = useState<FlyoutAccount | null>(null);
  const [refreshToken, setRefreshToken] = useState(0);

  const isDark = theme === 'dark';

  const refreshMainFlyout = () => {
    setRefreshToken((previous) => previous + 1);
  };

  return (
    <div className={`size-full overflow-hidden ${isDark ? 'bg-[#1c1c1c]' : 'bg-[#e5e5e5]'}`}>
      <div className="h-full p-6 flex flex-col">
        <div className="flex items-center justify-between mb-4">
          <div className={isDark ? 'text-white/80' : 'text-[#1c1c1c]'}>
            <div className="text-[16px] font-medium">CodexBar Frontend Rebuild Start</div>
            <div className="text-[11px] opacity-70">主浮窗 + Overlay + 弹窗模型（Figma 交互层级）</div>
          </div>
          <div className="flex items-center gap-2">
            <Windows11Button
              variant="secondary"
              size="sm"
              theme={theme}
              onClick={() => setTheme((previous) => (previous === 'light' ? 'dark' : 'light'))}
            >
              {theme === 'light' ? '切换深色' : '切换浅色'}
            </Windows11Button>
            <Windows11Button
              variant="secondary"
              size="sm"
              theme={theme}
              onClick={() => setOverlayExpanded((previous) => !previous)}
            >
              {overlayExpanded ? '收起 Overlay' : '展开 Overlay'}
            </Windows11Button>
            <Windows11Button
              variant="secondary"
              size="sm"
              theme={theme}
              onClick={() => setOverlayAccountType((previous) => (previous === 'openai' ? 'compatible' : 'openai'))}
            >
              {overlayAccountType === 'openai' ? 'Overlay: OpenAI' : 'Overlay: Compatible'}
            </Windows11Button>
          </div>
        </div>

        <div className="flex-1 relative rounded-xl border border-black/10 overflow-hidden">
          <div
            className={`absolute inset-0 ${isDark ? 'bg-gradient-to-br from-[#202020] to-[#2c2c2c]' : 'bg-gradient-to-br from-[#f0f0f0] to-[#e0e0e0]'}`}
          />

          <div className="absolute left-6 top-6 z-10">
            <MainFlyout
              theme={theme}
              onOpenOAuth={() => setShowOAuth(true)}
              onOpenAddProvider={() => setShowAddProvider(true)}
              onOpenSettings={() => setShowSettings(true)}
              onOpenEditAccount={(account) => setEditingAccount(account)}
              refreshToken={refreshToken}
            />
          </div>

          <div className="absolute right-6 top-6 z-10">
            <CompactFloatingOverlay expanded={overlayExpanded} accountType={overlayAccountType} theme={theme} />
          </div>

          {showOAuth && (
            <div className="absolute inset-0 z-20 bg-black/30 flex items-center justify-center p-4">
              <OAuthDialog
                theme={theme}
                onClose={() => setShowOAuth(false)}
                onCompleted={() => {
                  refreshMainFlyout();
                }}
              />
            </div>
          )}

          {showAddProvider && (
            <div className="absolute inset-0 z-20 bg-black/30 flex items-center justify-center p-4">
              <AddProviderDialog
                theme={theme}
                onClose={() => setShowAddProvider(false)}
                onSaved={() => {
                  refreshMainFlyout();
                }}
              />
            </div>
          )}

          {showSettings && (
            <div className="absolute inset-0 z-20 bg-black/30 flex items-center justify-center p-4">
              <SettingsWindow
                theme={theme}
                onSaved={() => {
                  refreshMainFlyout();
                }}
              />
              <div className="absolute top-6 right-6">
                <Windows11Button variant="subtle" size="sm" theme={theme} onClick={() => setShowSettings(false)}>
                  关闭设置
                </Windows11Button>
              </div>
            </div>
          )}

          {editingAccount && (
            <div className="absolute inset-0 z-20 bg-black/30 flex items-center justify-center p-4">
              <EditAccountDialog
                theme={theme}
                account={editingAccount}
                onClose={() => setEditingAccount(null)}
                onSaved={() => {
                  refreshMainFlyout();
                }}
              />
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
