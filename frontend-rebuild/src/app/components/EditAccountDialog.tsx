import { useEffect, useState } from 'react';
import { Windows11Window } from './Windows11Window';
import { Windows11Button } from './Windows11Button';
import { Windows11Input } from './Windows11Input';
import { codexbarApi, type CompatibleProviderRequest, type EditAccountRequest } from '../api/codexbarApi';
import type { FlyoutAccount } from './MainFlyout';

interface EditAccountDialogProps {
  theme?: 'light' | 'dark';
  account: FlyoutAccount;
  onClose?: () => void;
  onSaved?: () => void;
}

export function EditAccountDialog({ theme = 'light', account, onClose, onSaved }: EditAccountDialogProps) {
  const isDark = theme === 'dark';
  const [accountLabel, setAccountLabel] = useState(account.name);
  const [providerName, setProviderName] = useState('');
  const [baseUrl, setBaseUrl] = useState(account.baseUrl ?? '');
  const [codexProviderId, setCodexProviderId] = useState('');
  const [apiKey, setApiKey] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');

  useEffect(() => {
    setAccountLabel(account.name);
    setProviderName('');
    setBaseUrl(account.baseUrl ?? '');
    setCodexProviderId('');
    setApiKey('');
    setError('');
    setMessage('');
  }, [account]);

  const runAction = async (action: () => Promise<{ ok: boolean; message: string }>, successCallback?: () => void) => {
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
    } catch (actionError) {
      setError(actionError instanceof Error ? actionError.message : 'Operation failed');
    } finally {
      setBusy(false);
    }
  };

  const testConnection = async () => {
    if (account.type !== 'compatible') {
      setMessage('OpenAI OAuth account does not require API probe.');
      return;
    }

    if (!baseUrl.trim()) {
      setError('Base URL is required to test compatible provider connection.');
      return;
    }

    if (apiKey.trim()) {
      const payload: CompatibleProviderRequest = {
        providerId: account.providerId,
        codexProviderId: codexProviderId.trim() || null,
        providerName: providerName.trim() || account.providerId,
        baseUrl: baseUrl.trim(),
        accountId: account.accountId,
        accountLabel: accountLabel.trim() || account.accountId,
        apiKey: apiKey.trim()
      };
      await runAction(() => codexbarApi.probeCompatibleProvider(payload));
      return;
    }

    await runAction(() => codexbarApi.probeAccounts(account.providerId, account.accountId));
  };

  const saveEdit = async () => {
    if (!accountLabel.trim()) {
      setError('Account label is required.');
      return;
    }

    if (account.type === 'compatible' && !baseUrl.trim()) {
      setError('Base URL is required for compatible provider account.');
      return;
    }

    const payload: EditAccountRequest = {
      providerId: account.providerId,
      accountId: account.accountId,
      accountLabel: accountLabel.trim(),
      providerName: account.type === 'compatible' ? (providerName.trim() || null) : null,
      baseUrl: account.type === 'compatible' ? baseUrl.trim() : null,
      codexProviderId: account.type === 'compatible' ? (codexProviderId.trim() || null) : null,
      apiKey: account.type === 'compatible' ? (apiKey.trim() || null) : null
    };

    await runAction(() => codexbarApi.editAccount(payload), () => {
      onSaved?.();
      onClose?.();
    });
  };

  return (
    <Windows11Window title="Edit Account" width={640} height={620} theme={theme}>
      <div className="h-full flex flex-col">
        <div className="flex-1 overflow-y-auto p-6">
          <div className={`mb-4 p-3 rounded border ${isDark ? 'bg-[#e6f3ff]/10 border-[#60cdff]/30' : 'bg-[#e6f3ff] border-[#b3d9ff]'}`}>
            <div className={`text-[12px] leading-relaxed ${isDark ? 'text-white/90' : 'text-[#1c1c1c]'}`}>
              Update account metadata without rewriting historical sessions. Changes only affect future activations.
            </div>
          </div>

          <div className="space-y-4">
            <div>
              <label className={`block text-[12px] mb-1.5 ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>Provider ID</label>
              <Windows11Input value={account.providerId} readOnly theme={theme} />
            </div>

            <div>
              <label className={`block text-[12px] mb-1.5 ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>Account ID</label>
              <Windows11Input value={account.accountId} readOnly theme={theme} />
            </div>

            <div>
              <label className={`block text-[12px] mb-1.5 ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>Account Label *</label>
              <Windows11Input value={accountLabel} onChange={setAccountLabel} theme={theme} />
            </div>

            {account.type === 'openai' ? (
              <div>
                <label className={`block text-[12px] mb-1.5 ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>OpenAI Email</label>
                <Windows11Input value={account.email ?? ''} readOnly theme={theme} />
              </div>
            ) : (
              <>
                <div>
                  <label className={`block text-[12px] mb-1.5 ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>Base URL *</label>
                  <Windows11Input value={baseUrl} onChange={setBaseUrl} theme={theme} placeholder="https://api.example.com/v1" />
                </div>

                <div>
                  <label className={`block text-[12px] mb-1.5 ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>Provider Name (optional)</label>
                  <Windows11Input
                    value={providerName}
                    onChange={setProviderName}
                    theme={theme}
                    placeholder="Leave empty to keep current name"
                  />
                </div>

                <div>
                  <label className={`block text-[12px] mb-1.5 ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>Codex Provider ID (optional)</label>
                  <Windows11Input
                    value={codexProviderId}
                    onChange={setCodexProviderId}
                    theme={theme}
                    placeholder="Leave empty to keep current value"
                  />
                </div>

                <div>
                  <label className={`block text-[12px] mb-1.5 ${isDark ? 'text-white/60' : 'text-[#605e5c]'}`}>New API Key (optional)</label>
                  <Windows11Input
                    value={apiKey}
                    onChange={setApiKey}
                    theme={theme}
                    type="password"
                    placeholder="Leave empty to keep current key"
                  />
                </div>
              </>
            )}
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
          <Windows11Button variant="subtle" size="md" theme={theme} onClick={onClose} disabled={busy}>
            Cancel
          </Windows11Button>
          <div className="flex gap-2">
            {account.type === 'compatible' && (
              <Windows11Button variant="secondary" size="md" theme={theme} onClick={() => void testConnection()} disabled={busy}>
                Test Connection
              </Windows11Button>
            )}
            <Windows11Button variant="primary" size="md" theme={theme} onClick={() => void saveEdit()} disabled={busy}>
              Save
            </Windows11Button>
          </div>
        </div>
      </div>
    </Windows11Window>
  );
}
