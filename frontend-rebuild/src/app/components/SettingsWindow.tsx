import { useEffect, useRef, useState } from 'react';
import { Windows11Window } from './Windows11Window';
import { Windows11Button } from './Windows11Button';
import { Windows11Input } from './Windows11Input';
import { codexbarApi, type SettingsDto, type SettingsSaveRequest } from '../api/codexbarApi';

interface SettingsWindowProps {
  theme?: 'light' | 'dark';
  onSaved?: () => void;
}

function toSaveRequest(settings: SettingsDto): SettingsSaveRequest {
  return {
    codexDesktopPath: settings.codexDesktopPath,
    codexCliPath: settings.codexCliPath,
    accountSortMode: settings.accountSortMode,
    activationBehavior: settings.activationBehavior,
    openAiAccountMode: settings.openAiAccountMode,
    startupEnabled: settings.startupEnabled
  };
}

export function SettingsWindow({ theme = 'light', onSaved }: SettingsWindowProps) {
  const isDark = theme === 'dark';
  const [settings, setSettings] = useState<SettingsDto | null>(null);
  const [confirmBeforeActivation, setConfirmBeforeActivation] = useState(false);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');
  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const historyInputRef = useRef<HTMLInputElement | null>(null);

  const loadSettings = async () => {
    setBusy(true);
    setError('');
    try {
      const next = await codexbarApi.getSettings();
      setSettings(next);
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : '读取设置失败');
    } finally {
      setBusy(false);
    }
  };

  useEffect(() => {
    void loadSettings();
  }, []);

  const saveSettings = async (nextSettings: SettingsDto) => {
    setBusy(true);
    setError('');
    try {
      const result = await codexbarApi.saveSettings(toSaveRequest(nextSettings));
      setSettings(nextSettings);
      setMessage(result.message);
      onSaved?.();
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : '保存设置失败');
    } finally {
      setBusy(false);
    }
  };

  const updateSettings = (mutator: (current: SettingsDto) => SettingsDto, saveImmediately = false) => {
    setSettings((previous) => {
      if (!previous) {
        return previous;
      }
      const next = mutator(previous);
      if (saveImmediately) {
        void saveSettings(next);
      }
      return next;
    });
  };

  const runCommand = async (action: () => Promise<{ ok: boolean; message: string }>, successRefresh = false) => {
    setBusy(true);
    setError('');
    setMessage('');
    try {
      const result = await action();
      if (result.ok) {
        setMessage(result.message);
        if (successRefresh) {
          await loadSettings();
          onSaved?.();
        }
      } else {
        setError(result.message);
      }
    } catch (commandError) {
      setError(commandError instanceof Error ? commandError.message : '操作失败');
    } finally {
      setBusy(false);
    }
  };

  const downloadBlob = (fileName: string, blob: Blob) => {
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
  };

  const exportCsv = async () => {
    setBusy(true);
    setError('');
    setMessage('');
    try {
      const { fileName, blob } = await codexbarApi.exportAccountsCsv(false);
      downloadBlob(fileName, blob);
      setMessage(`已导出: ${fileName}`);
    } catch (exportError) {
      setError(exportError instanceof Error ? exportError.message : '导出失败');
    } finally {
      setBusy(false);
    }
  };

  const importCsv = async (file: File) => {
    setBusy(true);
    setError('');
    setMessage('');
    try {
      const result = await codexbarApi.importAccountsCsv(file);
      if (result.ok) {
        setMessage(result.message);
        await loadSettings();
        onSaved?.();
      } else {
        setError(result.message);
      }
    } catch (importError) {
      setError(importError instanceof Error ? importError.message : '导入失败');
    } finally {
      setBusy(false);
    }
  };

  const exportHistory = async () => {
    setBusy(true);
    setError('');
    setMessage('');
    try {
      const { fileName, blob } = await codexbarApi.exportHistoryZip(true);
      downloadBlob(fileName, blob);
      setMessage(`已导出历史会话: ${fileName}`);
    } catch (exportError) {
      setError(exportError instanceof Error ? exportError.message : '历史会话导出失败');
    } finally {
      setBusy(false);
    }
  };

  const importHistory = async (file: File) => {
    if (!window.confirm('导入会合并 sessions、archived_sessions 和 session_index.jsonl，不会触碰 config.toml、auth.json 或密钥。建议先关闭正在运行的 Codex 后再继续。')) {
      return;
    }

    setBusy(true);
    setError('');
    setMessage('');
    try {
      const result = await codexbarApi.importHistoryZip(file);
      if (result.ok) {
        setMessage(result.message);
      } else {
        setError(result.message);
      }
    } catch (importError) {
      setError(importError instanceof Error ? importError.message : '历史会话导入失败');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Windows11Window title="设置" width={900} height={760} theme={theme}>
      <div className="h-full overflow-y-auto">
        <div className="p-6">
          <div className="mb-6">
            <h3 className={`text-[15px] font-medium mb-3 ${isDark ? 'text-white/90' : 'text-[#1c1c1c]'}`}>路径</h3>
            <div className="space-y-3">
              <div>
                <label className={`block text-[12px] mb-1.5 ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>Codex Desktop 路径</label>
                <div className="flex gap-2">
                  <Windows11Input
                    value={settings?.codexDesktopPath ?? ''}
                    className="flex-1"
                    theme={theme}
                    onChange={(value) => updateSettings((current) => ({ ...current, codexDesktopPath: value }))}
                    onBlur={() => {
                      if (settings) {
                        void saveSettings(settings);
                      }
                    }}
                  />
                  <Windows11Button variant="secondary" size="md" theme={theme} disabled>
                    浏览
                  </Windows11Button>
                  <Windows11Button
                    variant="secondary"
                    size="md"
                    theme={theme}
                    disabled={busy}
                    onClick={() =>
                      void runCommand(async () => {
                        const result = await codexbarApi.detectDesktop(settings?.codexDesktopPath ?? '');
                        if (result.ok && settings) {
                          updateSettings((current) => ({ ...current, codexDesktopPath: result.message }));
                        }
                        return result;
                      })
                    }
                  >
                    探测
                  </Windows11Button>
                </div>
              </div>
              <div>
                <label className={`block text-[12px] mb-1.5 ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>Codex CLI 路径</label>
                <div className="flex gap-2">
                  <Windows11Input
                    value={settings?.codexCliPath ?? ''}
                    className="flex-1"
                    theme={theme}
                    onChange={(value) => updateSettings((current) => ({ ...current, codexCliPath: value }))}
                    onBlur={() => {
                      if (settings) {
                        void saveSettings(settings);
                      }
                    }}
                  />
                  <Windows11Button variant="secondary" size="md" theme={theme} disabled>
                    浏览
                  </Windows11Button>
                  <Windows11Button
                    variant="secondary"
                    size="md"
                    theme={theme}
                    disabled={busy}
                    onClick={() =>
                      void runCommand(async () => {
                        const result = await codexbarApi.detectCli(settings?.codexCliPath ?? '');
                        if (result.ok && settings) {
                          const firstLine = result.message.split('\n')[0] ?? result.message;
                          updateSettings((current) => ({ ...current, codexCliPath: firstLine }));
                        }
                        return result;
                      })
                    }
                  >
                    探测
                  </Windows11Button>
                </div>
              </div>
            </div>
          </div>

          <div className="mb-6">
            <h3 className={`text-[15px] font-medium mb-3 ${isDark ? 'text-white/90' : 'text-[#1c1c1c]'}`}>测试启动</h3>
            <div className="flex gap-2">
              <Windows11Button
                variant="secondary"
                size="md"
                theme={theme}
                disabled={busy || !settings}
                onClick={() =>
                  settings &&
                  void runCommand(() =>
                    codexbarApi.launchTarget({
                      codexDesktopPath: settings.codexDesktopPath,
                      codexCliPath: settings.codexCliPath,
                      target: 'desktop'
                    })
                  )
                }
              >
                测试启动 Desktop
              </Windows11Button>
              <Windows11Button
                variant="secondary"
                size="md"
                theme={theme}
                disabled={busy || !settings}
                onClick={() =>
                  settings &&
                  void runCommand(() =>
                    codexbarApi.launchTarget({
                      codexDesktopPath: settings.codexDesktopPath,
                      codexCliPath: settings.codexCliPath,
                      target: 'cli'
                    })
                  )
                }
              >
                测试启动 CLI
              </Windows11Button>
            </div>
          </div>

          <div className="mb-6">
            <h3 className={`text-[15px] font-medium mb-3 ${isDark ? 'text-white/90' : 'text-[#1c1c1c]'}`}>账号排序方式</h3>
            <div className="space-y-2">
              <label className="flex items-center gap-2 cursor-default">
                <input
                  type="radio"
                  name="sort"
                  className="w-4 h-4"
                  checked={settings?.accountSortMode === 'manual'}
                  onChange={() => updateSettings((current) => ({ ...current, accountSortMode: 'manual' }), true)}
                />
                <span className={`text-[13px] ${isDark ? 'text-white/90' : 'text-[#1c1c1c]'}`}>手动排序（支持拖动）</span>
              </label>
              <label className="flex items-center gap-2 cursor-default">
                <input
                  type="radio"
                  name="sort"
                  className="w-4 h-4"
                  checked={settings?.accountSortMode === 'usage'}
                  onChange={() => updateSettings((current) => ({ ...current, accountSortMode: 'usage' }), true)}
                />
                <span className={`text-[13px] ${isDark ? 'text-white/90' : 'text-[#1c1c1c]'}`}>按使用量排序</span>
              </label>
              <label className="flex items-center gap-2 cursor-default">
                <input type="radio" name="sort" className="w-4 h-4" disabled />
                <span className={`text-[13px] ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>按账号名称（待接入）</span>
              </label>
            </div>
          </div>

          <div className="mb-6">
            <h3 className={`text-[15px] font-medium mb-3 ${isDark ? 'text-white/90' : 'text-[#1c1c1c]'}`}>激活后行为</h3>
            <div className="space-y-2">
              <label className="flex items-center gap-2 cursor-default">
                <input
                  type="checkbox"
                  className="w-4 h-4"
                  checked={settings?.activationBehavior === 'launch'}
                  onChange={(event) =>
                    updateSettings((current) => ({ ...current, activationBehavior: event.target.checked ? 'launch' : 'write-only' }), true)
                  }
                />
                <span className={`text-[13px] ${isDark ? 'text-white/90' : 'text-[#1c1c1c]'}`}>激活后自动启动 Codex</span>
              </label>
              <label className="flex items-center gap-2 cursor-default">
                <input
                  type="checkbox"
                  className="w-4 h-4"
                  checked={confirmBeforeActivation}
                  onChange={(event) => setConfirmBeforeActivation(event.target.checked)}
                />
                <span className={`text-[13px] ${isDark ? 'text-white/90' : 'text-[#1c1c1c]'}`}>激活前显示确认（本地 UI 预留）</span>
              </label>
            </div>
          </div>

          <div className="mb-6">
            <h3 className={`text-[15px] font-medium mb-3 ${isDark ? 'text-white/90' : 'text-[#1c1c1c]'}`}>OpenAI 账号模式</h3>
            <div className="space-y-2">
              <label className="flex items-center gap-2 cursor-default">
                <input
                  type="radio"
                  name="oauthMode"
                  className="w-4 h-4"
                  checked={settings?.openAiAccountMode === 'manual'}
                  onChange={() => updateSettings((current) => ({ ...current, openAiAccountMode: 'manual' }), true)}
                />
                <span className={`text-[13px] ${isDark ? 'text-white/90' : 'text-[#1c1c1c]'}`}>手动切换</span>
              </label>
              <label className="flex items-center gap-2 cursor-default">
                <input
                  type="radio"
                  name="oauthMode"
                  className="w-4 h-4"
                  checked={settings?.openAiAccountMode === 'gateway'}
                  onChange={() => updateSettings((current) => ({ ...current, openAiAccountMode: 'gateway' }), true)}
                />
                <span className={`text-[13px] ${isDark ? 'text-white/90' : 'text-[#1c1c1c]'}`}>聚合网关（自动选优）</span>
              </label>
            </div>

            {settings?.openAiAccountMode === 'gateway' && settings.gatewayPreview && (
              <div className={`mt-3 p-4 rounded border ${isDark ? 'bg-[#60cdff]/10 border-[#60cdff]/30' : 'bg-[#e6f3ff] border-[#b3d9ff]'}`}>
                  <div className={`text-[12px] ${isDark ? 'text-white/90' : 'text-[#1c1c1c]'}`}>
                    预览：请求账号 {settings.gatewayPreview.requestedAccountLabel} -&gt; 实际选择 {settings.gatewayPreview.resolvedAccountLabel}
                  </div>
                <div className={`mt-1 text-[11px] ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>{settings.gatewayPreview.decisionMessage}</div>
              </div>
            )}
          </div>

          <div className="mb-6">
            <h3 className={`text-[15px] font-medium mb-3 ${isDark ? 'text-white/90' : 'text-[#1c1c1c]'}`}>导入/导出</h3>
            <div className="flex flex-wrap gap-2">
              <Windows11Button variant="secondary" size="md" theme={theme} onClick={() => void exportCsv()} disabled={busy}>
                导出 CSV
              </Windows11Button>
              <Windows11Button variant="secondary" size="md" theme={theme} onClick={() => fileInputRef.current?.click()} disabled={busy}>
                从 CSV 导入
              </Windows11Button>
              <Windows11Button variant="secondary" size="md" theme={theme} onClick={() => void exportHistory()} disabled={busy}>
                导出历史 ZIP
              </Windows11Button>
              <Windows11Button variant="secondary" size="md" theme={theme} onClick={() => historyInputRef.current?.click()} disabled={busy}>
                导入历史 ZIP
              </Windows11Button>
              <input
                ref={fileInputRef}
                className="hidden"
                type="file"
                accept=".csv,text/csv"
                onChange={(event) => {
                  const file = event.target.files?.[0];
                  if (file) {
                    void importCsv(file);
                  }
                  event.target.value = '';
                }}
              />
              <input
                ref={historyInputRef}
                className="hidden"
                type="file"
                accept=".zip,application/zip"
                onChange={(event) => {
                  const file = event.target.files?.[0];
                  if (file) {
                    void importHistory(file);
                  }
                  event.target.value = '';
                }}
              />
            </div>
            <div className={`mt-2 p-3 rounded border ${isDark ? 'bg-[#faa21b]/10 border-[#faa21b]/30' : 'bg-[#fff4ce] border-[#fde7a8]'}`}>
              <div className={`text-[11px] ${isDark ? 'text-white/80' : 'text-[#3d3d3d]'}`}>
                账号 CSV 只迁移账号配置；历史 ZIP 只迁移 sessions、archived_sessions 和 session_index.jsonl，不包含密钥，也不会改写 config.toml 或 auth.json。
              </div>
            </div>
          </div>

          <div className="mb-6">
            <h3 className={`text-[15px] font-medium mb-3 ${isDark ? 'text-white/90' : 'text-[#1c1c1c]'}`}>启动</h3>
            <label className="flex items-center gap-2 cursor-default">
              <input
                type="checkbox"
                className="w-4 h-4"
                checked={settings?.startupEnabled ?? false}
                onChange={(event) => updateSettings((current) => ({ ...current, startupEnabled: event.target.checked }), true)}
              />
              <span className={`text-[13px] ${isDark ? 'text-white/90' : 'text-[#1c1c1c]'}`}>登录 Windows 时自动启动</span>
            </label>
          </div>

          {(message || error) && (
            <div className={`mb-6 p-3 rounded border ${error
              ? (isDark ? 'bg-[#c42b1c]/10 border-[#c42b1c]/30' : 'bg-[#fef6f6] border-[#f1b9b9]')
              : (isDark ? 'bg-[#107c10]/10 border-[#107c10]/30' : 'bg-[#f0f9f0] border-[#c3e6c3]')}`}>
              <div className={`text-[12px] ${error
                ? (isDark ? 'text-[#ff9999]' : 'text-[#c42b1c]')
                : (isDark ? 'text-[#6ccf6c]' : 'text-[#107c10]')}`}>
                {error || message}
              </div>
            </div>
          )}

          <div className={`p-4 rounded border ${isDark ? 'bg-white/5 border-white/10' : 'bg-[#f3f3f3] border-[#0000001a]'}`}>
            <div className={`text-[12px] leading-relaxed space-y-2 ${isDark ? 'text-white/70' : 'text-[#605e5c]'}`}>
              <p>CodexBar 是账号切换辅助工具，不替代 Codex 本身。</p>
              <p>· 切换操作会更新 config.toml 和 auth.json 的当前激活态</p>
              <p>· 仅影响新启动的 Codex 会话</p>
              <p>· sessions / archived_sessions 保持不变</p>
              <p>· 共享 .codex 历史池保持不拆分</p>
            </div>
          </div>
        </div>
      </div>
    </Windows11Window>
  );
}
