import { useEffect, useRef, useState } from 'react';
import { DndProvider, useDrag, useDrop } from 'react-dnd';
import { HTML5Backend } from 'react-dnd-html5-backend';
import { Windows11Window } from './Windows11Window';
import { Windows11Button } from './Windows11Button';
import { IconButton } from './IconButton';
import { ModeSwitch } from './ModeSwitch';
import { codexbarApi, type DashboardAccountDto, type SettingsDto, type SettingsSaveRequest } from '../api/codexbarApi';

export interface FlyoutAccount {
  id: string;
  providerId: string;
  accountId: string;
  name: string;
  type: 'openai' | 'compatible';
  email?: string;
  baseUrl?: string;
  isActive: boolean;
  status: 'online' | 'offline' | 'checking';
  usage5h?: number;
  usageWeekly?: number;
  usage5hRefreshText?: string;
  usageWeeklyRefreshText?: string;
  usageDaily?: number;
  usageWeeklyTokens?: number;
  usageMonthly?: number;
}

const ITEM_TYPE = 'ACCOUNT';

interface DraggableAccountProps {
  account: FlyoutAccount;
  index: number;
  moveAccount: (dragIndex: number, hoverIndex: number) => void;
  theme?: 'light' | 'dark';
  busy?: boolean;
  onActivate: (account: FlyoutAccount) => void;
  onLaunch: (account: FlyoutAccount) => void;
  onProbe: (account: FlyoutAccount) => void;
  onEdit: (account: FlyoutAccount) => void;
  onDelete: (account: FlyoutAccount) => void;
}

function DraggableAccount({
  account,
  index,
  moveAccount,
  theme = 'light',
  busy = false,
  onActivate,
  onLaunch,
  onProbe,
  onEdit,
  onDelete
}: DraggableAccountProps) {
  const isDark = theme === 'dark';
  const [{ isDragging }, drag] = useDrag({
    type: ITEM_TYPE,
    item: { index },
    collect: (monitor) => ({
      isDragging: monitor.isDragging()
    })
  });

  const [, drop] = useDrop({
    accept: ITEM_TYPE,
    hover: (item: { index: number }) => {
      if (item.index !== index) {
        moveAccount(item.index, index);
        item.index = index;
      }
    }
  });

  const statusColors = {
    online: 'bg-[#107c10]',
    offline: 'bg-[#c42b1c]',
    checking: 'bg-[#faa21b]'
  };

  const getUsageColor = (percent: number) =>
    percent < 50 ? '#107c10' : percent < 80 ? '#faa21b' : '#c42b1c';

  return (
    <div
      ref={(node) => drag(drop(node))}
      className={`mb-2 p-2.5 rounded border transition-all cursor-move ${
        isDark
          ? (account.isActive
              ? 'bg-white/10 border-[#60cdff]'
              : 'bg-white/5 border-white/10 hover:border-white/20 hover:bg-white/8')
          : (account.isActive
              ? 'bg-[#f3f3f3] border-[#0067c0]'
              : 'bg-white border-[#0000001a] hover:border-[#00000033] hover:bg-[#f9f9f9]')
      } ${isDragging ? 'opacity-50' : 'opacity-100'} ${busy ? 'pointer-events-none' : ''}`}
    >
      <div className="flex items-start justify-between mb-2">
        <div className="flex-1 min-w-0 flex items-center gap-2">
          <svg width="11" height="11" viewBox="0 0 11 11" className={`flex-shrink-0 ${isDark ? 'text-white/40' : 'text-[#605e5c]'}`}>
            <rect x="0.5" y="0.5" width="4" height="4" fill="currentColor" rx="1" />
            <rect x="6" y="0.5" width="4" height="4" fill="currentColor" rx="1" />
            <rect x="0.5" y="6" width="4" height="4" fill="currentColor" rx="1" />
            <rect x="6" y="6" width="4" height="4" fill="currentColor" rx="1" />
          </svg>
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-1.5 mb-1">
              <span className={`text-[13px] font-medium truncate ${isDark ? 'text-white/90' : 'text-[#1c1c1c]'}`}>{account.name}</span>
              {account.isActive && (
                <span className={`px-1.5 py-0.5 text-[9px] rounded ${isDark ? 'bg-[#60cdff] text-[#1c1c1c]' : 'bg-[#0067c0] text-white'}`}>当前激活</span>
              )}
              <span
                className={`px-1.5 py-0.5 text-[9px] text-white rounded ${
                  account.type === 'openai' ? (isDark ? 'bg-white/20' : 'bg-[#605e5c]') : (isDark ? 'bg-[#8764b8]/80' : 'bg-[#8764b8]')
                }`}
              >
                {account.type === 'openai' ? 'OpenAI' : '兼容 Provider'}
              </span>
              <div
                className={`w-1.5 h-1.5 rounded-full ${statusColors[account.status]}`}
                title={account.status === 'online' ? '在线' : account.status === 'offline' ? '离线' : '检测中'}
              />
            </div>
            <div className="flex items-center justify-between gap-2">
              <div className={`text-[10px] truncate ${isDark ? 'text-white/50' : 'text-[#605e5c]'}`}>{account.email || account.baseUrl}</div>
              <div className="flex items-center gap-1 flex-shrink-0">
                {!account.isActive && (
                  <Windows11Button variant="secondary" size="sm" theme={theme} onClick={() => onActivate(account)} disabled={busy}>
                    使用
                  </Windows11Button>
                )}
                {account.isActive && (
                  <Windows11Button variant="primary" size="sm" theme={theme} onClick={() => onLaunch(account)} disabled={busy}>
                    启动
                  </Windows11Button>
                )}
                <IconButton icon="detect" tooltip="探测 API" theme={theme} onClick={() => onProbe(account)} />
                <IconButton icon="edit" tooltip="编辑账号" theme={theme} onClick={() => onEdit(account)} />
                <IconButton icon="delete" tooltip="删除账号" theme={theme} onClick={() => onDelete(account)} />
              </div>
            </div>
          </div>
        </div>
      </div>

      {account.type === 'openai' ? (
        <div className="mb-2 space-y-1.5">
          <div>
            <div className="flex items-center justify-between text-[9px] mb-1">
              <span className={isDark ? 'text-white/50' : 'text-[#605e5c]'}>{account.usage5hRefreshText || '5h 额度'}</span>
              <span className={isDark ? 'text-white/80' : 'text-[#1c1c1c]'}>{account.usage5h || 0}%</span>
            </div>
            <div className={`h-1 rounded-full overflow-hidden ${isDark ? 'bg-white/10' : 'bg-[#e9e9e9]'}`}>
              <div
                className="h-full rounded-full transition-all"
                style={{ width: `${account.usage5h || 0}%`, backgroundColor: getUsageColor(account.usage5h || 0) }}
              />
            </div>
          </div>
          <div>
            <div className="flex items-center justify-between text-[9px] mb-1">
              <span className={isDark ? 'text-white/50' : 'text-[#605e5c]'}>{account.usageWeeklyRefreshText || '周额度'}</span>
              <span className={isDark ? 'text-white/80' : 'text-[#1c1c1c]'}>{account.usageWeekly || 0}%</span>
            </div>
            <div className={`h-1 rounded-full overflow-hidden ${isDark ? 'bg-white/10' : 'bg-[#e9e9e9]'}`}>
              <div
                className="h-full rounded-full transition-all"
                style={{ width: `${account.usageWeekly || 0}%`, backgroundColor: getUsageColor(account.usageWeekly || 0) }}
              />
            </div>
          </div>
        </div>
      ) : (
        <div className="mb-2">
          <div className="grid grid-cols-3 gap-1.5 text-[9px]">
            <div>
              <div className={`mb-1 ${isDark ? 'text-white/50' : 'text-[#605e5c]'}`}>1 天</div>
              <div className={`px-1.5 py-1 rounded text-center ${isDark ? 'bg-white/10 text-white/80' : 'bg-[#f9f9f9] text-[#1c1c1c]'}`}>
                {(account.usageDaily ?? 0).toLocaleString()}
              </div>
            </div>
            <div>
              <div className={`mb-1 ${isDark ? 'text-white/50' : 'text-[#605e5c]'}`}>1 周</div>
              <div className={`px-1.5 py-1 rounded text-center ${isDark ? 'bg-white/10 text-white/80' : 'bg-[#f9f9f9] text-[#1c1c1c]'}`}>
                {(account.usageWeeklyTokens ?? 0).toLocaleString()}
              </div>
            </div>
            <div>
              <div className={`mb-1 ${isDark ? 'text-white/50' : 'text-[#605e5c]'}`}>1 月</div>
              <div className={`px-1.5 py-1 rounded text-center ${isDark ? 'bg-white/10 text-white/80' : 'bg-[#f9f9f9] text-[#1c1c1c]'}`}>
                {(account.usageMonthly ?? 0).toLocaleString()}
              </div>
            </div>
          </div>
          <div className={`text-[8px] mt-1 text-center ${isDark ? 'text-white/40' : 'text-[#605e5c]'}`}>Token 用量</div>
        </div>
      )}
    </div>
  );
}

function toAccount(account: DashboardAccountDto): FlyoutAccount {
  return {
    id: `${account.providerId}/${account.accountId}`,
    providerId: account.providerId,
    accountId: account.accountId,
    name: account.name,
    type: account.type,
    email: account.email ?? undefined,
    baseUrl: account.baseUrl ?? undefined,
    isActive: account.isActive,
    status: account.status,
    usage5h: account.usage5h ?? undefined,
    usageWeekly: account.usageWeekly ?? undefined,
    usage5hRefreshText: account.usage5hRefreshText ?? undefined,
    usageWeeklyRefreshText: account.usageWeeklyRefreshText ?? undefined,
    usageDaily: account.usageDaily ?? undefined,
    usageWeeklyTokens: account.usageWeeklyTokens ?? undefined,
    usageMonthly: account.usageMonthly ?? undefined
  };
}

function toSaveRequest(settings: SettingsDto, routingMode: 'manual' | 'auto'): SettingsSaveRequest {
  return {
    codexDesktopPath: settings.codexDesktopPath,
    codexCliPath: settings.codexCliPath,
    accountSortMode: settings.accountSortMode,
    activationBehavior: settings.activationBehavior,
    openAiAccountMode: routingMode === 'auto' ? 'gateway' : 'manual',
    startupEnabled: settings.startupEnabled
  };
}

interface MainFlyoutProps {
  theme?: 'light' | 'dark';
  onOpenOAuth?: () => void;
  onOpenAddProvider?: () => void;
  onOpenSettings?: () => void;
  onOpenEditAccount?: (account: FlyoutAccount) => void;
  refreshToken?: number;
}

export function MainFlyout({
  theme = 'light',
  onOpenOAuth,
  onOpenAddProvider,
  onOpenSettings,
  onOpenEditAccount,
  refreshToken = 0
}: MainFlyoutProps) {
  const isDark = theme === 'dark';
  const [accounts, setAccounts] = useState<FlyoutAccount[]>([]);
  const [isCheckingAll, setIsCheckingAll] = useState(false);
  const [routingMode, setRoutingMode] = useState<'manual' | 'auto'>('manual');
  const [lastRefreshText, setLastRefreshText] = useState('');
  const [footerNote, setFooterNote] = useState('切换仅影响新会话 · 现有会话保持不变');
  const [quotaStatus, setQuotaStatus] = useState('');
  const [settings, setSettings] = useState<SettingsDto | null>(null);
  const [message, setMessage] = useState('');
  const [busyAccountKey, setBusyAccountKey] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const reorderTimerRef = useRef<number | null>(null);

  const loadDashboard = async () => {
    setIsLoading(true);
    try {
      const [dashboard, nextSettings] = await Promise.all([
        codexbarApi.getDashboard(),
        codexbarApi.getSettings()
      ]);
      setAccounts(dashboard.accounts.map(toAccount));
      setRoutingMode(dashboard.routingMode);
      setLastRefreshText(dashboard.lastRefreshText);
      setFooterNote(dashboard.footerNote);
      setQuotaStatus(dashboard.quotaStatusText ?? '');
      setSettings(nextSettings);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '读取主浮窗数据失败');
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    void loadDashboard();
    return () => {
      if (reorderTimerRef.current !== null) {
        window.clearTimeout(reorderTimerRef.current);
      }
    };
  }, []);

  useEffect(() => {
    if (refreshToken !== 0) {
      void loadDashboard();
    }
  }, [refreshToken]);

  const scheduleReorder = (nextAccounts: FlyoutAccount[]) => {
    if (reorderTimerRef.current !== null) {
      window.clearTimeout(reorderTimerRef.current);
    }
    reorderTimerRef.current = window.setTimeout(async () => {
      try {
        const orderedKeys = nextAccounts.map((account) => `${account.providerId}/${account.accountId}`);
        const result = await codexbarApi.reorderAccounts(orderedKeys);
        setMessage(result.message);
      } catch (error) {
        setMessage(error instanceof Error ? error.message : '保存账号顺序失败');
      } finally {
        reorderTimerRef.current = null;
      }
    }, 450);
  };

  const moveAccount = (dragIndex: number, hoverIndex: number) => {
    setAccounts((previous) => {
      const next = [...previous];
      const [removed] = next.splice(dragIndex, 1);
      next.splice(hoverIndex, 0, removed);
      scheduleReorder(next);
      return next;
    });
  };

  const runAccountAction = async (account: FlyoutAccount, action: () => Promise<{ message: string }>) => {
    setBusyAccountKey(account.id);
    try {
      const result = await action();
      setMessage(result.message);
      await loadDashboard();
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '操作失败');
    } finally {
      setBusyAccountKey(null);
    }
  };

  const checkAllApis = async () => {
    setIsCheckingAll(true);
    setAccounts((previous) => previous.map((account) => ({ ...account, status: 'checking' })));
    try {
      const result = await codexbarApi.probeAccounts();
      setMessage(result.message);
      await loadDashboard();
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '探测失败');
    } finally {
      setIsCheckingAll(false);
    }
  };

  const handleRoutingModeChange = async (nextMode: 'manual' | 'auto') => {
    setRoutingMode(nextMode);
    if (!settings) {
      return;
    }

    try {
      const result = await codexbarApi.saveSettings(toSaveRequest(settings, nextMode));
      setSettings((previous) => (previous ? { ...previous, openAiAccountMode: nextMode === 'auto' ? 'gateway' : 'manual' } : previous));
      setMessage(result.message);
      await loadDashboard();
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '保存路由模式失败');
    }
  };

  const activeAccount = accounts.find((account) => account.isActive);

  return (
    <DndProvider backend={HTML5Backend}>
      <Windows11Window title="CodexBar" width={420} height={720} theme={theme}>
        <div className="h-full flex flex-col">
          <div className="px-3 pt-3 pb-2 grid grid-cols-2 gap-2">
            <Windows11Button variant="primary" size="sm" theme={theme} onClick={onOpenOAuth}>
              登录 OpenAI
            </Windows11Button>
            <Windows11Button variant="secondary" size="sm" theme={theme} onClick={onOpenAddProvider}>
              添加兼容 Provider
            </Windows11Button>
            <Windows11Button variant="secondary" size="sm" theme={theme} onClick={() => void checkAllApis()} disabled={isCheckingAll}>
              {isCheckingAll ? '探测中...' : '探测所有 API'}
            </Windows11Button>
            <Windows11Button variant="subtle" size="sm" theme={theme} onClick={onOpenSettings}>
              设置
            </Windows11Button>
          </div>

          <div className="mx-3 mb-3">
            <ModeSwitch value={routingMode} onChange={(value) => void handleRoutingModeChange(value)} theme={theme} />
          </div>

          {activeAccount && (
            <div className={`mx-3 mb-3 p-3 rounded border ${isDark ? 'bg-white/5 border-white/10' : 'bg-[#f9f9f9] border-[#0000000d]'}`}>
              <div className={`text-[10px] mb-1 ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>当前激活</div>
              <div className={`text-[13px] font-medium mb-2 ${isDark ? 'text-white/90' : 'text-[#1c1c1c]'}`}>
                {activeAccount.name} - {activeAccount.type === 'openai' ? 'OpenAI' : '兼容 Provider'}
              </div>

              {activeAccount.type === 'openai' ? (
                <>
                  <div className="flex items-center justify-between text-[10px] mb-1">
                    <span className={isDark ? 'text-white/60' : 'text-[#605e5c]'}>5h 额度</span>
                    <span className={activeAccount.usage5h! < 50 ? 'text-[#107c10]' : activeAccount.usage5h! < 80 ? 'text-[#faa21b]' : 'text-[#c42b1c]'}>
                      {(100 - (activeAccount.usage5h || 0)).toFixed(1)}% 可用
                    </span>
                  </div>
                  <div className={`h-1 rounded-full overflow-hidden mb-2 ${isDark ? 'bg-white/10' : 'bg-[#e9e9e9]'}`}>
                    <div
                      className="h-full rounded-full transition-all"
                      style={{
                        width: `${activeAccount.usage5h || 0}%`,
                        backgroundColor: (activeAccount.usage5h || 0) < 50 ? '#107c10' : (activeAccount.usage5h || 0) < 80 ? '#faa21b' : '#c42b1c'
                      }}
                    />
                  </div>
                  <div className="flex items-center justify-between text-[10px] mb-1">
                    <span className={isDark ? 'text-white/60' : 'text-[#605e5c]'}>周额度</span>
                    <span className={activeAccount.usageWeekly! < 50 ? 'text-[#107c10]' : activeAccount.usageWeekly! < 80 ? 'text-[#faa21b]' : 'text-[#c42b1c]'}>
                      {(100 - (activeAccount.usageWeekly || 0)).toFixed(1)}% 可用
                    </span>
                  </div>
                  <div className={`h-1 rounded-full overflow-hidden ${isDark ? 'bg-white/10' : 'bg-[#e9e9e9]'}`}>
                    <div
                      className="h-full rounded-full transition-all"
                      style={{
                        width: `${activeAccount.usageWeekly || 0}%`,
                        backgroundColor: (activeAccount.usageWeekly || 0) < 50 ? '#107c10' : (activeAccount.usageWeekly || 0) < 80 ? '#faa21b' : '#c42b1c'
                      }}
                    />
                  </div>
                </>
              ) : (
                <div className="grid grid-cols-3 gap-2 text-[10px]">
                  <div className="text-center">
                    <div className={`mb-1 ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>1 天</div>
                    <div className={`font-medium ${isDark ? 'text-white/90' : 'text-[#1c1c1c]'}`}>{(activeAccount.usageDaily ?? 0).toLocaleString()}</div>
                  </div>
                  <div className="text-center">
                    <div className={`mb-1 ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>1 周</div>
                    <div className={`font-medium ${isDark ? 'text-white/90' : 'text-[#1c1c1c]'}`}>{(activeAccount.usageWeeklyTokens ?? 0).toLocaleString()}</div>
                  </div>
                  <div className="text-center">
                    <div className={`mb-1 ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>1 月</div>
                    <div className={`font-medium ${isDark ? 'text-white/90' : 'text-[#1c1c1c]'}`}>{(activeAccount.usageMonthly ?? 0).toLocaleString()}</div>
                  </div>
                </div>
              )}

              <div className={`mt-2 text-[10px] ${isDark ? 'text-white/50' : 'text-[#605e5c]'}`}>上次刷新: {lastRefreshText || '--'}</div>
              {quotaStatus && (
                <div className={`mt-1 text-[10px] ${isDark ? 'text-white/50' : 'text-[#605e5c]'}`}>{quotaStatus}</div>
              )}
            </div>
          )}

          <div
            className="flex-1 overflow-y-auto px-3"
            style={{
              scrollbarWidth: 'thin',
              scrollbarColor: isDark
                ? 'rgba(255, 255, 255, 0.3) rgba(255, 255, 255, 0.05)'
                : 'rgba(0, 0, 0, 0.3) rgba(0, 0, 0, 0.05)'
            }}
          >
            <style>{`
              .flex-1.overflow-y-auto::-webkit-scrollbar {
                width: 8px;
              }
              .flex-1.overflow-y-auto::-webkit-scrollbar-track {
                background: ${isDark ? 'rgba(255, 255, 255, 0.05)' : 'rgba(0, 0, 0, 0.05)'};
              }
              .flex-1.overflow-y-auto::-webkit-scrollbar-thumb {
                background: ${isDark ? 'rgba(255, 255, 255, 0.25)' : 'rgba(0, 0, 0, 0.25)'};
                border-radius: 4px;
              }
              .flex-1.overflow-y-auto::-webkit-scrollbar-thumb:hover {
                background: ${isDark ? 'rgba(255, 255, 255, 0.35)' : 'rgba(0, 0, 0, 0.35)'};
              }
            `}</style>
            <div className={`text-[10px] mb-2 px-1 flex items-center justify-between ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>
              <span>账号与 Provider ({accounts.length})</span>
              <span className="text-[9px]">{isLoading ? '加载中' : '拖动排序'}</span>
            </div>

            {accounts.map((account, index) => (
              <DraggableAccount
                key={account.id}
                account={account}
                index={index}
                moveAccount={moveAccount}
                theme={theme}
                busy={busyAccountKey === account.id}
                onActivate={(target) => void runAccountAction(target, () => codexbarApi.activateAccount(target.providerId, target.accountId))}
                onLaunch={(target) => void runAccountAction(target, () => codexbarApi.launchAccount(target.providerId, target.accountId))}
                onProbe={(target) => void runAccountAction(target, () => codexbarApi.probeAccounts(target.providerId, target.accountId))}
                onEdit={(target) => onOpenEditAccount?.(target)}
                onDelete={(target) => void runAccountAction(target, () => codexbarApi.deleteAccount(target.providerId, target.accountId))}
              />
            ))}
          </div>

          <div className={`px-3 py-2 border-t ${isDark ? 'border-white/10 bg-white/5' : 'border-[#0000000d] bg-[#f9f9f9]'}`}>
            <div className={`text-[9px] text-center ${isDark ? 'text-white/50' : 'text-[#605e5c]'}`}>
              {message || footerNote}
            </div>
          </div>
        </div>
      </Windows11Window>
    </DndProvider>
  );
}
