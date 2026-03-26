import { useEffect, useRef, useState } from 'react';
import {
  vodsApi,
  filesystemApi,
  type PipelineJob,
  isActive, isPending, isCompleted, isFailed,
  formatDuration,
} from '../api/client';

// ── Stage order used for determining per-step status ─────────────────────────

const STAGE_ORDER: Record<string, number> = {
  Pending: 0,
  DownloadingVod: 1,
  PendingDownloadChat: 2,
  DownloadingChat: 3,
  PendingRenderingChat: 4,
  RenderingChat: 5,
  PendingCombining: 6,
  Combining: 7,
  PendingUpload: 8,
  Uploading: 9,
  PendingArchiving: 10,
  Archiving: 11,
  // Both terminal states share the same index so that all pipeline steps
  // evaluate as "completed" for a Cancelled job.  The isCancelled path in
  // getStepStatus handles the subtle behavioural difference.
  Uploaded: 12,
  Cancelled: 12,
};

type StepStatus = 'completed' | 'active' | 'error' | 'pending';

interface PipelineStep {
  key: string;
  label: string;
  icon: string;
  /** Minimum STAGE_ORDER index at which this step is considered "completed". */
  completedAtIndex: number;
  /** STAGE_ORDER index at which this step is "active". */
  activeIndex: number;
}

const PIPELINE_STEPS: PipelineStep[] = [
  { key: 'downloadVod',  label: 'Download VOD',  icon: 'download',       completedAtIndex: 2,  activeIndex: 1 },
  { key: 'downloadChat', label: 'Download Chat', icon: 'chat',           completedAtIndex: 4,  activeIndex: 3 },
  { key: 'renderChat',   label: 'Render Chat',   icon: 'movie_filter',   completedAtIndex: 6,  activeIndex: 5 },
  { key: 'combine',      label: 'Combine',       icon: 'merge_type',     completedAtIndex: 8,  activeIndex: 7 },
  { key: 'archive',      label: 'Archive',       icon: 'archive',        completedAtIndex: 12, activeIndex: 11 },
];

function getStepStatus(step: PipelineStep, job: PipelineJob): StepStatus {
  const stageIdx = STAGE_ORDER[job.stage] ?? 0;
  const isCancelled = job.stage === 'Cancelled';

  if (isCancelled) {
    if (stageIdx >= step.completedAtIndex) return 'completed';
    return 'pending';
  }

  if (job.failed) {
    if (stageIdx > step.activeIndex) return 'completed';
    if (stageIdx === step.activeIndex) return 'error';
    return 'pending';
  }

  if (stageIdx >= step.completedAtIndex) return 'completed';
  if (stageIdx === step.activeIndex || stageIdx === step.activeIndex - 1) return 'active';
  if (stageIdx > step.activeIndex) return 'completed';
  return 'pending';
}

// Which file path(s) are relevant for each step (for "open in file explorer")
function getStepFilePaths(step: PipelineStep, job: PipelineJob): { label: string; path: string }[] {
  switch (step.key) {
    case 'downloadVod':
      return job.vodFilePath
        ? [{ label: 'VOD file', path: job.vodFilePath }]
        : job.archivedVodPath
          ? [{ label: 'Archived VOD', path: job.archivedVodPath }]
          : [];
    case 'downloadChat':
      return job.chatTextFilePath
        ? [{ label: 'Chat JSON', path: job.chatTextFilePath }]
        : job.archivedChatJsonPath
          ? [{ label: 'Archived Chat JSON', path: job.archivedChatJsonPath }]
          : [];
    case 'renderChat':
      return job.chatVideoFilePath
        ? [{ label: 'Chat render', path: job.chatVideoFilePath }]
        : job.archivedChatRenderPath
          ? [{ label: 'Archived Chat render', path: job.archivedChatRenderPath }]
          : [];
    case 'combine':
      return job.finalVideoFilePath
        ? [{ label: 'Final video', path: job.finalVideoFilePath }]
        : job.archivedFinalVideoPath
          ? [{ label: 'Archived final video', path: job.archivedFinalVideoPath }]
          : [];
    case 'archive': {
      const paths: { label: string; path: string }[] = [];
      if (job.archivedVodPath)        paths.push({ label: 'Archived VOD',        path: job.archivedVodPath });
      if (job.archivedChatJsonPath)   paths.push({ label: 'Archived Chat JSON',   path: job.archivedChatJsonPath });
      if (job.archivedChatRenderPath) paths.push({ label: 'Archived Chat render', path: job.archivedChatRenderPath });
      if (job.archivedFinalVideoPath) paths.push({ label: 'Archived final video', path: job.archivedFinalVideoPath });
      return paths;
    }
    default:
      return [];
  }
}

// ── Step Card ─────────────────────────────────────────────────────────────────

function StepCard({
  step,
  job,
  onRetryFromStage,
}: {
  step: PipelineStep;
  job: PipelineJob;
  onRetryFromStage: (stageKey: string) => void;
}) {
  const status = getStepStatus(step, job);
  const filePaths = getStepFilePaths(step, job);
  const [revealing, setRevealing] = useState<string | null>(null);

  async function reveal(path: string) {
    setRevealing(path);
    try {
      await filesystemApi.reveal(path);
    } catch {
      // ignore — server will open the OS file manager
    } finally {
      setRevealing(null);
    }
  }

  const statusConfig = {
    completed: {
      dotClass: 'bg-emerald-500',
      labelClass: 'text-emerald-400',
      label: 'Completed',
      borderClass: 'border-white/5',
      bgClass: '',
    },
    active: {
      dotClass: 'bg-primary animate-pulse',
      labelClass: 'text-primary',
      label: 'In Progress',
      borderClass: 'border-primary/20',
      bgClass: 'bg-primary/5',
    },
    error: {
      dotClass: 'bg-error',
      labelClass: 'text-error',
      label: 'Failed',
      borderClass: 'border-error/20',
      bgClass: 'bg-error/5',
    },
    pending: {
      dotClass: 'bg-on-surface-variant/20',
      labelClass: 'text-on-surface-variant/50',
      label: 'Pending',
      borderClass: 'border-white/5',
      bgClass: '',
    },
  }[status];

  const canRetry = status !== 'pending' && job.stage !== 'Cancelled';

  return (
    <div className={`rounded-lg border ${statusConfig.borderClass} ${statusConfig.bgClass} p-3 transition-all`}>
      <div className="flex items-center justify-between gap-2">
        {/* Icon + label */}
        <div className="flex items-center gap-2 min-w-0">
          <span className={`material-symbols-outlined text-base ${status === 'pending' ? 'text-on-surface-variant/30' : statusConfig.labelClass}`}>
            {step.icon}
          </span>
          <span className={`text-xs font-bold ${status === 'pending' ? 'text-on-surface-variant/50' : 'text-on-surface'}`}>
            {step.label}
          </span>
        </div>

        {/* Status badge */}
        <div className="flex items-center gap-1.5 flex-shrink-0">
          <span className={`h-1.5 w-1.5 rounded-full ${statusConfig.dotClass}`} />
          <span className={`text-[10px] font-bold uppercase ${statusConfig.labelClass}`}>{statusConfig.label}</span>
        </div>
      </div>

      {/* Error message */}
      {status === 'error' && job.failReason && (
        <div className="mt-2 px-2 py-1.5 bg-error/10 rounded text-[10px] text-error/80 leading-snug">
          {job.failReason}
        </div>
      )}

      {/* File path buttons */}
      {filePaths.length > 0 && (
        <div className="mt-2 flex flex-wrap gap-1.5">
          {filePaths.map(({ label, path }) => (
            <button
              key={path}
              onClick={() => reveal(path)}
              disabled={revealing === path}
              className="flex items-center gap-1 px-2 py-1 rounded bg-surface-container-highest hover:bg-surface-container-high text-on-surface-variant hover:text-on-surface transition-colors text-[10px] font-medium disabled:opacity-50"
              title={path}
            >
              <span className="material-symbols-outlined text-xs leading-none">folder_open</span>
              {revealing === path ? 'Opening…' : label}
            </button>
          ))}
        </div>
      )}

      {/* Retry button */}
      {canRetry && (
        <div className="mt-2 flex justify-end">
          <button
            onClick={() => onRetryFromStage(step.key)}
            className="flex items-center gap-1 px-2 py-1 rounded text-[10px] font-bold text-primary hover:bg-primary/10 transition-colors"
          >
            <span className="material-symbols-outlined text-xs leading-none">replay</span>
            Retry from here
          </button>
        </div>
      )}
    </div>
  );
}

// ── VOD Detail Panel ──────────────────────────────────────────────────────────

export default function VodDetailPanel({
  job,
  thumbnailUrl,
  onClose,
  onJobChanged,
}: {
  job: PipelineJob;
  thumbnailUrl?: string;
  onClose: () => void;
  onJobChanged: () => void;
}) {
  const panelRef = useRef<HTMLDivElement>(null);

  // Close on backdrop click
  useEffect(() => {
    function handleKey(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose();
    }
    document.addEventListener('keydown', handleKey);
    return () => document.removeEventListener('keydown', handleKey);
  }, [onClose]);

  async function handleRetryFromStage(stageKey: string) {
    try {
      await vodsApi.retryFromStage(job.vodId, stageKey);
      onJobChanged();
    } catch {
      // ignore
    }
  }

  const done = isCompleted(job);
  const failed = isFailed(job);
  const active = isActive(job);
  const pending = isPending(job);

  function overallStatusLabel() {
    if (failed) return `Failed (${job.failCount}/3)`;
    if (active) return 'In Progress';
    if (pending) return job.paused ? 'Paused' : 'Pending';
    if (done) return job.stage === 'Cancelled' ? 'Cancelled' : 'Completed';
    return job.stage;
  }

  function overallStatusColor() {
    if (failed) return 'text-error';
    if (active) return 'text-primary';
    if (done && job.stage !== 'Cancelled') return 'text-emerald-400';
    if (job.paused) return 'text-warning';
    return 'text-on-surface-variant/60';
  }

  function formatDate(iso: string) {
    if (!iso || iso.startsWith('0001')) return '—';
    return new Date(iso).toLocaleDateString('en-US', {
      year: 'numeric', month: 'short', day: 'numeric',
    });
  }

  return (
    /* Backdrop */
    <div
      className="fixed inset-0 z-50 flex items-stretch justify-end"
      onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}
    >
      {/* Scrim */}
      <div className="absolute inset-0 bg-black/50 backdrop-blur-sm" onClick={onClose} />

      {/* Panel */}
      <div
        ref={panelRef}
        className="relative z-10 flex flex-col w-full max-w-lg bg-surface-container-low border-l border-white/5 shadow-2xl overflow-y-auto"
        style={{ animation: 'slideInRight 0.2s ease-out' }}
      >
        {/* Header */}
        <div className="flex items-start gap-3 px-5 py-4 border-b border-white/5 bg-surface-container/50 sticky top-0 z-10">
          {thumbnailUrl && (
            <div className="h-12 w-20 flex-shrink-0 rounded-lg overflow-hidden">
              <img src={thumbnailUrl} alt={job.title} className="w-full h-full object-cover" />
            </div>
          )}
          <div className="flex-1 min-w-0">
            <h2 className="text-sm font-bold text-on-surface truncate leading-snug">{job.title || job.vodId}</h2>
            <div className="flex items-center gap-2 mt-0.5">
              <span className={`text-[10px] font-bold uppercase ${overallStatusColor()}`}>{overallStatusLabel()}</span>
              {job.channelName && (
                <>
                  <span className="text-on-surface-variant/30 text-[10px]">·</span>
                  <span className="text-[10px] text-on-surface-variant/60">{job.channelName}</span>
                </>
              )}
            </div>
          </div>
          <button
            onClick={onClose}
            className="flex-shrink-0 p-1.5 text-on-surface-variant hover:text-on-surface hover:bg-white/5 rounded transition-colors"
          >
            <span className="material-symbols-outlined text-lg">close</span>
          </button>
        </div>

        {/* Metadata */}
        <div className="px-5 py-4 border-b border-white/5">
          <h3 className="text-[10px] font-bold uppercase tracking-widest text-on-surface-variant mb-3">Metadata</h3>
          <div className="grid grid-cols-2 gap-x-4 gap-y-2">
            <MetaRow label="VOD ID" value={job.vodId} mono />
            <MetaRow label="Duration" value={formatDuration(job.duration)} />
            <MetaRow label="Channel" value={job.channelName || '—'} />
            <MetaRow label="Uploaded on Twitch" value={formatDate(job.createdAtUTC)} />
            <MetaRow label="Added to queue" value={formatDate(job.addedAtUTC)} />
            {job.youtubeVideoId && (
              <div className="col-span-2">
                <span className="text-[10px] uppercase tracking-widest text-on-surface-variant">YouTube</span>
                <a
                  href={`https://www.youtube.com/watch?v=${job.youtubeVideoId}`}
                  target="_blank"
                  rel="noreferrer"
                  className="block text-xs text-primary hover:underline truncate mt-0.5"
                >
                  youtube.com/watch?v={job.youtubeVideoId}
                </a>
              </div>
            )}
          </div>

          {/* Twitch link */}
          {job.vodUrl && (
            <a
              href={job.vodUrl}
              target="_blank"
              rel="noreferrer"
              className="mt-3 flex items-center gap-1.5 text-xs text-primary hover:underline"
            >
              <span className="material-symbols-outlined text-sm">open_in_new</span>
              View original VOD on Twitch
            </a>
          )}
        </div>

        {/* Pipeline Steps */}
        <div className="px-5 py-4 flex-1">
          <h3 className="text-[10px] font-bold uppercase tracking-widest text-on-surface-variant mb-3">Pipeline Steps</h3>
          <div className="flex flex-col gap-2">
            {PIPELINE_STEPS.map((step, i) => (
              <div key={step.key} className="flex gap-2">
                {/* Connector */}
                <div className="flex flex-col items-center pt-3">
                  <div className={`w-0.5 h-full ${i < PIPELINE_STEPS.length - 1 ? 'bg-white/5' : 'bg-transparent'}`} />
                </div>
                <div className="flex-1 pb-2">
                  <StepCard step={step} job={job} onRetryFromStage={handleRetryFromStage} />
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

function MetaRow({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div>
      <span className="text-[10px] uppercase tracking-widest text-on-surface-variant">{label}</span>
      <p className={`text-xs text-on-surface mt-0.5 truncate ${mono ? 'font-mono' : ''}`}>{value || '—'}</p>
    </div>
  );
}
