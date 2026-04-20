import { useEffect, useState } from 'react';
import { Windows11Window } from './Windows11Window';
import { Windows11Button } from './Windows11Button';
import { Windows11Input } from './Windows11Input';
import { codexbarApi, type OAuthStateDto } from '../api/codexbarApi';

interface OAuthDialogProps {
  theme?: 'light' | 'dark';
  onClose?: () => void;
  onCompleted?: () => void;
}

export function OAuthDialog({ theme = 'light', onClose, onCompleted }: OAuthDialogProps) {
  const isDark = theme === 'dark';
  const [state, setState] = useState<OAuthStateDto | null>(null);
  const [manualInput, setManualInput] = useState('');
  const [label, setLabel] = useState('');
  const [busy, setBusy] = useState(false);
  const [commandError, setCommandError] = useState('');
  const [commandSuccess, setCommandSuccess] = useState('');

  const refreshState = async () => {
    try {
      const latest = await codexbarApi.getOAuthState();
      setState(latest);
    } catch (error) {
      setCommandError(error instanceof Error ? error.message : '读取 OAuth 状态失败');
    }
  };

  useEffect(() => {
    void refreshState();
  }, []);

  useEffect(() => {
    if (!state?.isListening) {
      return;
    }

    const timer = window.setInterval(() => {
      void refreshState();
    }, 1500);

    return () => {
      window.clearInterval(timer);
    };
  }, [state?.isListening]);

  const runStateAction = async (action: () => Promise<OAuthStateDto>) => {
    setBusy(true);
    setCommandError('');
    setCommandSuccess('');
    try {
      const next = await action();
      setState(next);
      if (next.successMessage) {
        setCommandSuccess(next.successMessage);
      }
      if (next.errorMessage) {
        setCommandError(next.errorMessage);
      }
    } catch (error) {
      setCommandError(error instanceof Error ? error.message : 'OAuth 操作失败');
    } finally {
      setBusy(false);
    }
  };

  const completeOAuth = async () => {
    setBusy(true);
    setCommandError('');
    setCommandSuccess('');
    try {
      const result = await codexbarApi.completeOAuth(manualInput, label);
      if (result.ok) {
        setCommandSuccess(result.message);
        onCompleted?.();
      } else {
        setCommandError(result.message);
      }
      await refreshState();
    } catch (error) {
      setCommandError(error instanceof Error ? error.message : '保存 OpenAI 账号失败');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Windows11Window title="登录 OpenAI 账号" width={640} height={560} theme={theme}>
      <div className="h-full flex flex-col">
        <div className="flex-1 overflow-y-auto p-6">
          <div className={`mb-4 p-3 rounded border ${isDark ? 'bg-[#60cdff]/10 border-[#60cdff]/30' : 'bg-[#e6f3ff] border-[#b3d9ff]'}`}>
            <div className={`text-[12px] leading-relaxed ${isDark ? 'text-white/90' : 'text-[#1c1c1c]'}`}>
              通过浏览器完成 OpenAI OAuth 授权。CodexBar 会监听 localhost 回调；如果没有自动回调，可手工粘贴 URL 或 code。
            </div>
          </div>

          <div className="mb-4">
            <label className={`block text-[12px] mb-1.5 ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>授权 URL（只读）</label>
            <Windows11Input value={state?.authorizationUrl ?? ''} readOnly theme={theme} />
          </div>

          <div className="mb-4 flex gap-2">
            <Windows11Button variant="primary" size="md" theme={theme} onClick={() => void runStateAction(() => codexbarApi.openOAuthBrowser())} disabled={busy}>
              打开浏览器
            </Windows11Button>
            <Windows11Button variant="secondary" size="md" theme={theme} onClick={() => void runStateAction(() => codexbarApi.listenOAuthCallback())} disabled={busy}>
              监听 localhost:1455
            </Windows11Button>
          </div>

          <div className={`mb-4 p-3 rounded border ${isDark ? 'bg-white/5 border-white/10' : 'bg-[#f9f9f9] border-[#0000001a]'}`}>
            <div className="flex items-center gap-2">
              <div className={`w-2 h-2 rounded-full ${state?.isListening ? 'animate-pulse' : ''} ${isDark ? 'bg-[#60cdff]' : 'bg-[#0067c0]'}`} />
              <span className={`text-[12px] ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>
                {state?.statusMessage || '等待 OAuth 操作'}
              </span>
            </div>
          </div>

          <div className="mb-4">
            <label className={`block text-[12px] mb-1.5 ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>
              手工粘贴回调 URL 或 authorization code（fallback）
            </label>
            <Windows11Input multiline rows={4} placeholder="粘贴完整回调 URL 或单独 code..." theme={theme} value={manualInput} onChange={setManualInput} />
          </div>

          <div className="mb-4">
            <label className={`block text-[12px] mb-1.5 ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>
              账号标签（可选）
            </label>
            <Windows11Input placeholder="例如: 个人账号" theme={theme} value={label} onChange={setLabel} />
          </div>

          {(commandError || state?.errorMessage) && (
            <div className={`mb-3 p-3 rounded border ${isDark ? 'bg-[#c42b1c]/10 border-[#c42b1c]/30' : 'bg-[#fef6f6] border-[#f1b9b9]'}`}>
              <div className={`text-[12px] ${isDark ? 'text-[#ff9999]' : 'text-[#c42b1c]'}`}>错误: {commandError || state?.errorMessage}</div>
            </div>
          )}

          {(commandSuccess || state?.successMessage) && (
            <div className={`mb-3 p-3 rounded border ${isDark ? 'bg-[#107c10]/10 border-[#107c10]/30' : 'bg-[#f0f9f0] border-[#c3e6c3]'}`}>
              <div className={`text-[12px] ${isDark ? 'text-[#6ccf6c]' : 'text-[#107c10]'}`}>成功: {commandSuccess || state?.successMessage}</div>
            </div>
          )}
        </div>

        <div className={`px-6 py-4 border-t flex justify-between items-center ${
          isDark ? 'border-white/10 bg-white/5' : 'border-[#0000000d] bg-[#f9f9f9]'
        }`}>
          <Windows11Button variant="subtle" size="md" theme={theme} onClick={onClose}>
            取消
          </Windows11Button>
          <Windows11Button variant="primary" size="md" theme={theme} onClick={() => void completeOAuth()} disabled={busy}>
            完成登录
          </Windows11Button>
        </div>
      </div>
    </Windows11Window>
  );
}
