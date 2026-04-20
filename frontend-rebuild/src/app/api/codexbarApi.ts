export type ConnectionStatus = 'online' | 'offline' | 'checking';
export type RoutingMode = 'manual' | 'auto';

export interface CommandResult {
  ok: boolean;
  message: string;
}

export interface DashboardAccountDto {
  providerId: string;
  accountId: string;
  name: string;
  type: 'openai' | 'compatible';
  email: string | null;
  baseUrl: string | null;
  isActive: boolean;
  status: ConnectionStatus;
  usage5h: number | null;
  usageWeekly: number | null;
  usageDaily: number | null;
  usageWeeklyTokens: number | null;
  usageMonthly: number | null;
}

export interface DashboardDto {
  homePath: string;
  routingMode: RoutingMode;
  model: string;
  reasoningEffort: string;
  lastRefreshText: string;
  quotaStatusText: string | null;
  footerNote: string;
  accounts: DashboardAccountDto[];
}

export interface GatewayPreviewDto {
  requestedAccountLabel: string;
  resolvedAccountLabel: string;
  decisionMessage: string;
}

export interface SettingsDto {
  appStatePath: string;
  codexHomePath: string;
  codexDesktopPath: string;
  codexCliPath: string;
  accountSortMode: 'usage' | 'manual';
  activationBehavior: 'launch' | 'write-only';
  openAiAccountMode: 'gateway' | 'manual';
  startupEnabled: boolean;
  gatewayPreview: GatewayPreviewDto | null;
}

export interface OAuthStateDto {
  authorizationUrl: string;
  isListening: boolean;
  hasCapturedTokens: boolean;
  isCompleted: boolean;
  statusMessage: string;
  errorMessage: string | null;
  successMessage: string | null;
}

export interface SettingsSaveRequest {
  codexDesktopPath: string;
  codexCliPath: string;
  accountSortMode: 'usage' | 'manual';
  activationBehavior: 'launch' | 'write-only';
  openAiAccountMode: 'gateway' | 'manual';
  startupEnabled: boolean;
}

export interface LaunchRequest {
  codexDesktopPath: string;
  codexCliPath: string;
  target: 'desktop' | 'cli';
}

export interface CompatibleProviderRequest {
  providerId: string;
  codexProviderId: string | null;
  providerName: string;
  baseUrl: string;
  accountId: string;
  accountLabel: string;
  apiKey: string;
}

export interface EditAccountRequest {
  providerId: string;
  accountId: string;
  accountLabel: string;
  providerName: string | null;
  baseUrl: string | null;
  codexProviderId: string | null;
  apiKey: string | null;
}

const baseUrl = (import.meta.env.VITE_CODEXBAR_API_BASE as string | undefined)?.trim() || 'http://127.0.0.1:5057';

async function requestJson<T>(path: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers ?? {});
  if (!headers.has('Content-Type') && init?.body) {
    headers.set('Content-Type', 'application/json');
  }

  const response = await fetch(`${baseUrl}${path}`, {
    ...init,
    headers
  });

  const text = await response.text();
  const data = text ? tryParseJson(text) : null;
  if (!response.ok) {
    const message = typeof data === 'object' && data && 'message' in data
      ? String((data as { message?: string }).message ?? `Request failed (${response.status})`)
      : `Request failed (${response.status})`;
    throw new Error(message);
  }

  return data as T;
}

function tryParseJson(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}

export const codexbarApi = {
  getDashboard() {
    return requestJson<DashboardDto>('/api/dashboard');
  },

  getSettings() {
    return requestJson<SettingsDto>('/api/settings');
  },

  saveSettings(payload: SettingsSaveRequest) {
    return requestJson<CommandResult>('/api/settings/save', {
      method: 'POST',
      body: JSON.stringify(payload)
    });
  },

  detectDesktop(path: string) {
    return requestJson<CommandResult>('/api/settings/detect-desktop', {
      method: 'POST',
      body: JSON.stringify({ path })
    });
  },

  detectCli(path: string) {
    return requestJson<CommandResult>('/api/settings/detect-cli', {
      method: 'POST',
      body: JSON.stringify({ path })
    });
  },

  launchTarget(payload: LaunchRequest) {
    return requestJson<CommandResult>('/api/settings/launch', {
      method: 'POST',
      body: JSON.stringify(payload)
    });
  },

  async exportAccountsCsv(includeSecrets = false) {
    const response = await fetch(`${baseUrl}/api/settings/export?includeSecrets=${includeSecrets ? 'true' : 'false'}`);
    if (!response.ok) {
      throw new Error(`Export failed (${response.status})`);
    }

    const disposition = response.headers.get('content-disposition') ?? '';
    const match = disposition.match(/filename="?([^"]+)"?/i);
    const fileName = match?.[1] ?? (includeSecrets ? 'codexbar-accounts-with-secrets.csv' : 'codexbar-accounts.csv');
    const blob = await response.blob();
    return { fileName, blob };
  },

  async importAccountsCsv(file: File) {
    const formData = new FormData();
    formData.append('file', file);

    const response = await fetch(`${baseUrl}/api/settings/import`, {
      method: 'POST',
      body: formData
    });
    const text = await response.text();
    const data = text ? tryParseJson(text) : null;
    if (!response.ok) {
      const message = typeof data === 'object' && data && 'message' in data
        ? String((data as { message?: string }).message ?? `Import failed (${response.status})`)
        : `Import failed (${response.status})`;
      throw new Error(message);
    }
    return data as CommandResult;
  },

  activateAccount(providerId: string, accountId: string) {
    return requestJson<CommandResult>('/api/accounts/activate', {
      method: 'POST',
      body: JSON.stringify({ providerId, accountId })
    });
  },

  launchAccount(providerId: string, accountId: string) {
    return requestJson<CommandResult>('/api/accounts/launch', {
      method: 'POST',
      body: JSON.stringify({ providerId, accountId })
    });
  },

  probeAccounts(providerId?: string, accountId?: string) {
    return requestJson<CommandResult>('/api/accounts/probe', {
      method: 'POST',
      body: JSON.stringify({ providerId, accountId })
    });
  },

  editAccount(payload: EditAccountRequest) {
    return requestJson<CommandResult>('/api/accounts/edit', {
      method: 'POST',
      body: JSON.stringify(payload)
    });
  },

  deleteAccount(providerId: string, accountId: string) {
    return requestJson<CommandResult>(`/api/accounts/${encodeURIComponent(providerId)}/${encodeURIComponent(accountId)}`, {
      method: 'DELETE'
    });
  },

  reorderAccounts(orderedKeys: string[]) {
    return requestJson<CommandResult>('/api/accounts/reorder', {
      method: 'POST',
      body: JSON.stringify({ orderedKeys })
    });
  },

  addCompatibleProvider(payload: CompatibleProviderRequest) {
    return requestJson<CommandResult>('/api/providers/compatible', {
      method: 'POST',
      body: JSON.stringify(payload)
    });
  },

  probeCompatibleProvider(payload: CompatibleProviderRequest) {
    return requestJson<CommandResult>('/api/providers/compatible/probe', {
      method: 'POST',
      body: JSON.stringify(payload)
    });
  },

  getOAuthState() {
    return requestJson<OAuthStateDto>('/api/oauth/state');
  },

  openOAuthBrowser() {
    return requestJson<OAuthStateDto>('/api/oauth/open-browser', {
      method: 'POST'
    });
  },

  listenOAuthCallback() {
    return requestJson<OAuthStateDto>('/api/oauth/listen', {
      method: 'POST'
    });
  },

  completeOAuth(callbackInput: string, label: string) {
    return requestJson<CommandResult>('/api/oauth/complete', {
      method: 'POST',
      body: JSON.stringify({ callbackInput, label })
    });
  }
};
