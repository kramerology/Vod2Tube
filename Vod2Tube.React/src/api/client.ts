const BASE = '/api';

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    headers: { 'Content-Type': 'application/json', ...init?.headers },
    ...init,
  });
  if (!res.ok) {
    let message = `${res.status} ${res.statusText}`;
    try {
      const body = await res.json();
      if (typeof body?.error === 'string') message = body.error;
    } catch { /* not JSON — keep the status/statusText message */ }
    throw new Error(message);
  }
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

// ── Domain types ──────────────────────────────────────────────────────────────

export interface Channel {
  id: number;
  channelName: string;
  addedAtUTC: string;
  active: boolean;
  youTubeAccountId: number | null;
}

export interface PipelineJob {
  vodId: string;
  stage: string;
  description: string;
  paused: boolean;
  failed: boolean;
  failReason: string;
  failCount: number;
  youtubeVideoId: string;
  percentComplete: number | null;
  estimatedMinutesRemaining: number | null;
  title: string;
  channelName: string;
  createdAtUTC: string;
  duration: string;
  vodUrl: string;
  addedAtUTC: string;
  thumbnailUrl?: string;
  // Working-copy file paths (may be empty after archiving)
  vodFilePath: string;
  chatTextFilePath: string;
  chatVideoFilePath: string;
  finalVideoFilePath: string;
  // Archive destination paths (populated after the Archiving stage)
  archivedVodPath: string;
  archivedChatJsonPath: string;
  archivedChatRenderPath: string;
  archivedFinalVideoPath: string;
}

// ── Channel endpoints ─────────────────────────────────────────────────────────

export const channelsApi = {
  getAll: () => request<Channel[]>('/channels'),

  create: (channel: Pick<Channel, 'channelName' | 'active'> & { youTubeAccountId?: number | null }) =>
    request<Channel>('/channels', {
      method: 'POST',
      body: JSON.stringify(channel),
    }),

  update: (channel: Channel) =>
    request<Channel>(`/channels/${channel.id}`, {
      method: 'PUT',
      body: JSON.stringify(channel),
    }),

  delete: (id: number) =>
    request<void>(`/channels/${id}`, { method: 'DELETE' }),

  getAvatarUrls: () =>
    request<Record<string, string>>('/channels/avatars'),
};

// ── YouTube Accounts ──────────────────────────────────────────────────────────

export interface YouTubeAccount {
  id: number;
  name: string;
  addedAtUTC: string;
  channelTitle: string;
  isAuthorized: boolean;
}

export const accountsApi = {
  getAll: () => request<YouTubeAccount[]>('/accounts'),

  get: (id: number) => request<YouTubeAccount>(`/accounts/${id}`),

  create: (name: string, clientSecretsJson: string) =>
    request<YouTubeAccount>('/accounts', {
      method: 'POST',
      body: JSON.stringify({ name, clientSecretsJson }),
    }),

  update: (id: number, name: string) =>
    request<void>(`/accounts/${id}`, {
      method: 'PUT',
      body: JSON.stringify({ name }),
    }),

  delete: (id: number) =>
    request<void>(`/accounts/${id}`, { method: 'DELETE' }),

  authorize: (id: number) =>
    request<{ authorizationUrl: string }>(`/accounts/${id}/authorize`, { method: 'POST' }),

  revoke: (id: number) =>
    request<void>(`/accounts/${id}/revoke`, { method: 'POST' }),
};

// ── Pipeline / VODs endpoints ─────────────────────────────────────────────────

export const vodsApi = {
  getAll: () => request<PipelineJob[]>('/vods'),
  getActive: () => request<PipelineJob[]>('/vods/active'),
  getCompleted: () => request<PipelineJob[]>('/vods/completed'),

  pause: (vodId: string) =>
    request<void>(`/vods/${encodeURIComponent(vodId)}/pause`, { method: 'POST' }),

  resume: (vodId: string) =>
    request<void>(`/vods/${encodeURIComponent(vodId)}/resume`, { method: 'POST' }),

  cancel: (vodId: string) =>
    request<void>(`/vods/${encodeURIComponent(vodId)}/cancel`, { method: 'POST' }),

  retry: (vodId: string) =>
    request<void>(`/vods/${encodeURIComponent(vodId)}/retry`, { method: 'POST' }),

  retryFromStage: (vodId: string, stage: string) =>
    request<void>(`/vods/${encodeURIComponent(vodId)}/retry/${encodeURIComponent(stage)}`, { method: 'POST' }),

  getThumbnailUrls: (vodIds: string[]) =>
    request<Record<string, string>>(`/vods/thumbnails?ids=${vodIds.map(encodeURIComponent).join(',')}`),
};

// ── Settings ──────────────────────────────────────────────────────────────────

export interface AppSettings {
  twitchDownloaderCliPath: string;
  ffmpegPath: string;
  ffprobePath: string;
  ytDlpPath: string;

  tempDir: string;
  vodDownloadDir: string;
  chatRenderDir: string;
  finalVideoDir: string;

  chatWidth: number;
  chatFontSize: number;
  chatUpdateRate: number;

  archiveVodEnabled: boolean;
  archiveVodDir: string;
  archiveChatJsonEnabled: boolean;
  archiveChatJsonDir: string;
  archiveChatRenderEnabled: boolean;
  archiveChatRenderDir: string;
  archiveFinalVideoEnabled: boolean;
  archiveFinalVideoDir: string;
}

export interface ExecutableRequirementStatus {
  settingName: string;
  displayName: string;
  path: string;
  exists: boolean;
}

export interface ExecutableReadinessStatus {
  isReady: boolean;
  checkedAtUtc: string;
  message: string;
  requiredExecutables: ExecutableRequirementStatus[];
}

export const settingsApi = {
  get: () => request<AppSettings>('/settings'),
  getExecutableStatus: () => request<ExecutableReadinessStatus>('/settings/executables'),
  save: (settings: AppSettings) =>
    request<AppSettings>('/settings', {
      method: 'PUT',
      body: JSON.stringify(settings),
    }),
};

// ── Filesystem browser ────────────────────────────────────────────────────────

export interface DirectoryEntry {
  name: string;
  fullPath: string;
}

export interface FileEntry {
  name: string;
  fullPath: string;
}

export interface BrowseResult {
  currentPath: string;
  parentPath: string | null;
  directories: DirectoryEntry[];
  files: FileEntry[];
  drives: string[] | null; // Windows only
}

export const filesystemApi = {
  browse: (path?: string) =>
    request<BrowseResult>(
      `/filesystem/browse${path ? `?path=${encodeURIComponent(path)}` : ''}`
    ),
};

// ── Stage helpers ─────────────────────────────────────────────────────────────

const ACTIVE_STAGES = new Set([
  'DownloadingVod', 'DownloadingChat', 'RenderingChat', 'Combining', 'Uploading', 'Archiving',
]);
const PENDING_STAGES = new Set([
  'Pending', 'PendingDownloadChat', 'PendingRenderingChat', 'PendingCombining', 'PendingUpload', 'PendingArchiving',
]);
const COMPLETED_STAGES = new Set(['Uploaded', 'Cancelled']);

export const isActive = (j: PipelineJob) => !j.failed && ACTIVE_STAGES.has(j.stage);
export const isPending = (j: PipelineJob) => !j.failed && PENDING_STAGES.has(j.stage);
export const isCompleted = (j: PipelineJob) => COMPLETED_STAGES.has(j.stage);
export const isFailed = (j: PipelineJob) => j.failed && !COMPLETED_STAGES.has(j.stage);

export function formatDuration(iso: string): string {
  // ISO 8601 duration like "PT1H23M45S" or .NET TimeSpan "01:23:45"
  if (iso.includes(':')) {
    const parts = iso.split(':').map(Number);
    if (parts.length === 3) {
      const [h, m, s] = parts;
      return h > 0 ? `${h}h ${m}m` : `${m}m ${Math.floor(s)}s`;
    }
  }
  return iso;
}

export function stageLabel(j: PipelineJob): string {
  if (j.failed) return `Failed (${j.failCount}/3)`;
  if (j.stage === 'Uploaded') return 'Uploaded';
  if (j.stage === 'Cancelled') return 'Cancelled';
  return j.stage.replace(/([A-Z])/g, ' $1').trim();
}

export function formatEta(minutes: number): string {
  if (minutes < 1) return '< 1 min';
  if (minutes < 60) return `~${Math.ceil(minutes)} min`;
  const h = Math.floor(minutes / 60);
  const m = Math.ceil(minutes % 60);
  return `~${h}h ${m}m`;
}
