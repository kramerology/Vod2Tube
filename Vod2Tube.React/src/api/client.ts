const BASE = '/api';

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    headers: { 'Content-Type': 'application/json', ...init?.headers },
    ...init,
  });
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`);
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

// ── Domain types ──────────────────────────────────────────────────────────────

export interface Channel {
  id: number;
  channelName: string;
  addedAtUTC: string;
  active: boolean;
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
  title: string;
  channelName: string;
  createdAtUTC: string;
  duration: string;
  vodUrl: string;
  addedAtUTC: string;
}

// ── Channel endpoints ─────────────────────────────────────────────────────────

export const channelsApi = {
  getAll: () => request<Channel[]>('/channels'),

  create: (channel: Pick<Channel, 'channelName' | 'active'>) =>
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
};

// ── Stage helpers ─────────────────────────────────────────────────────────────

const ACTIVE_STAGES = new Set([
  'DownloadingVod', 'DownloadingChat', 'RenderingChat', 'Combining', 'Uploading',
]);
const PENDING_STAGES = new Set([
  'Pending', 'PendingDownloadChat', 'PendingRenderingChat', 'PendingCombining', 'PendingUpload',
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
