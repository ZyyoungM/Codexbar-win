import { useState } from 'react';
import { Windows11Window } from './Windows11Window';
import { Windows11Button } from './Windows11Button';
import { Windows11Input } from './Windows11Input';
import { codexbarApi, type CompatibleProviderRequest } from '../api/codexbarApi';

interface AddProviderDialogProps {
  theme?: 'light' | 'dark';
  onClose?: () => void;
  onSaved?: () => void;
}

export function AddProviderDialog({ theme = 'light', onClose, onSaved }: AddProviderDialogProps) {
  const [showAdvanced, setShowAdvanced] = useState(false);
  const [form, setForm] = useState<CompatibleProviderRequest>({
    providerId: '',
    codexProviderId: 'openai',
    providerName: '',
    baseUrl: '',
    accountId: '',
    accountLabel: '',
    apiKey: ''
  });
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');
  const isDark = theme === 'dark';

  const updateField = <K extends keyof CompatibleProviderRequest>(key: K, value: CompatibleProviderRequest[K]) => {
    setForm((previous) => ({ ...previous, [key]: value }));
  };

  const validateRequired = () => {
    if (!form.providerId || !form.providerName || !form.baseUrl || !form.accountId || !form.accountLabel || !form.apiKey) {
      setError('请填写所有必填项');
      return false;
    }
    return true;
  };

  const runCommand = async (action: () => Promise<{ ok: boolean; message: string }>, successCallback?: () => void) => {
    if (!validateRequired()) {
      return;
    }

    setBusy(true);
    setError('');
    setMessage('');
    try {
      const result = await action();
      if (result.ok) {
        setMessage(result.message);
        successCallback?.();
      } else {
        setError(result.message);
      }
    } catch (commandError) {
      setError(commandError instanceof Error ? commandError.message : '操作失败');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Windows11Window title="添加兼容 Provider" width={640} height={620} theme={theme}>
      <div className="h-full flex flex-col">
        <div className="flex-1 overflow-y-auto p-6">
          <div className={`mb-4 p-3 rounded border ${isDark ? 'bg-[#faa21b]/10 border-[#faa21b]/30' : 'bg-[#fff4ce] border-[#fde7a8]'}`}>
            <div className={`text-[12px] leading-relaxed ${isDark ? 'text-white/90' : 'text-[#3d3d3d]'}`}>
              为兼容 OpenAI API 的第三方 Provider 添加配置。请确保 API Key 有效且 Base URL 正确。
            </div>
          </div>

          <div className="space-y-4">
            <div>
              <label className={`block text-[12px] mb-1.5 ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>
                Provider ID <span className={isDark ? 'text-[#ff9999]' : 'text-[#c42b1c]'}>*</span>
              </label>
              <Windows11Input
                placeholder="例如: deepseek, anthropic, custom_provider"
                theme={theme}
                value={form.providerId}
                onChange={(value) => updateField('providerId', value)}
              />
              <div className={`mt-1 text-[11px] ${isDark ? 'text-white/50' : 'text-[#605e5c]'}`}>唯一标识符，仅小写字母、数字、下划线</div>
            </div>

            <div>
              <label className={`block text-[12px] mb-1.5 ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>
                Provider 名称 <span className={isDark ? 'text-[#ff9999]' : 'text-[#c42b1c]'}>*</span>
              </label>
              <Windows11Input
                placeholder="例如: DeepSeek, Claude via Anthropic"
                theme={theme}
                value={form.providerName}
                onChange={(value) => updateField('providerName', value)}
              />
            </div>

            <div>
              <label className={`block text-[12px] mb-1.5 ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>
                Base URL <span className={isDark ? 'text-[#ff9999]' : 'text-[#c42b1c]'}>*</span>
              </label>
              <Windows11Input
                placeholder="https://api.example.com/v1"
                theme={theme}
                value={form.baseUrl}
                onChange={(value) => updateField('baseUrl', value)}
              />
            </div>

            <div>
              <label className={`block text-[12px] mb-1.5 ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>
                账号 ID <span className={isDark ? 'text-[#ff9999]' : 'text-[#c42b1c]'}>*</span>
              </label>
              <Windows11Input
                placeholder="例如: main_account, test_key"
                theme={theme}
                value={form.accountId}
                onChange={(value) => updateField('accountId', value)}
              />
              <div className={`mt-1 text-[11px] ${isDark ? 'text-white/50' : 'text-[#605e5c]'}`}>该 Provider 下的账号标识</div>
            </div>

            <div>
              <label className={`block text-[12px] mb-1.5 ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>
                账号显示名 <span className={isDark ? 'text-[#ff9999]' : 'text-[#c42b1c]'}>*</span>
              </label>
              <Windows11Input
                placeholder="例如: DeepSeek 测试账号"
                theme={theme}
                value={form.accountLabel}
                onChange={(value) => updateField('accountLabel', value)}
              />
            </div>

            <div>
              <label className={`block text-[12px] mb-1.5 ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>
                API Key <span className={isDark ? 'text-[#ff9999]' : 'text-[#c42b1c]'}>*</span>
              </label>
              <Windows11Input
                placeholder="sk-..."
                theme={theme}
                type="password"
                value={form.apiKey}
                onChange={(value) => updateField('apiKey', value)}
              />
            </div>

            <div className={`pt-2 border-t ${isDark ? 'border-white/10' : 'border-[#0000000d]'}`}>
              <button
                className={`text-[12px] hover:underline cursor-default flex items-center gap-1 ${isDark ? 'text-[#60cdff]' : 'text-[#0067c0]'}`}
                onClick={() => setShowAdvanced(!showAdvanced)}
                type="button"
              >
                <span>{showAdvanced ? '▼' : '▶'}</span>
                <span>高级选项</span>
              </button>

              {showAdvanced && (
                <div className="mt-3 space-y-3">
                  <div>
                    <label className={`block text-[12px] mb-1.5 ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>
                      Codex Provider ID（可选）
                    </label>
                    <Windows11Input
                      placeholder="默认: openai"
                      theme={theme}
                      value={form.codexProviderId ?? ''}
                      onChange={(value) => updateField('codexProviderId', value || null)}
                    />
                    <div className={`mt-1 text-[11px] ${isDark ? 'text-white/50' : 'text-[#605e5c]'}`}>
                      大多数兼容 OpenAI 的 API 维持 "openai" 即可。
                    </div>
                  </div>
                </div>
              )}
            </div>
          </div>

          {error && (
            <div className={`mt-4 p-3 rounded border ${isDark ? 'bg-[#c42b1c]/10 border-[#c42b1c]/30' : 'bg-[#fef6f6] border-[#f1b9b9]'}`}>
              <div className={`text-[12px] ${isDark ? 'text-[#ff9999]' : 'text-[#c42b1c]'}`}>{error}</div>
            </div>
          )}
          {message && (
            <div className={`mt-4 p-3 rounded border ${isDark ? 'bg-[#107c10]/10 border-[#107c10]/30' : 'bg-[#f0f9f0] border-[#c3e6c3]'}`}>
              <div className={`text-[12px] ${isDark ? 'text-[#6ccf6c]' : 'text-[#107c10]'}`}>{message}</div>
            </div>
          )}
        </div>

        <div className={`px-6 py-4 border-t flex justify-between items-center ${
          isDark ? 'border-white/10 bg-white/5' : 'border-[#0000000d] bg-[#f9f9f9]'
        }`}>
          <Windows11Button variant="subtle" size="md" theme={theme} onClick={onClose}>
            取消
          </Windows11Button>
          <div className="flex gap-2">
            <Windows11Button
              variant="secondary"
              size="md"
              theme={theme}
              onClick={() => void runCommand(() => codexbarApi.probeCompatibleProvider(form))}
              disabled={busy}
            >
              测试连接
            </Windows11Button>
            <Windows11Button
              variant="primary"
              size="md"
              theme={theme}
              onClick={() =>
                void runCommand(() => codexbarApi.addCompatibleProvider(form), () => {
                  onSaved?.();
                })
              }
              disabled={busy}
            >
              添加
            </Windows11Button>
          </div>
        </div>
      </div>
    </Windows11Window>
  );
}
