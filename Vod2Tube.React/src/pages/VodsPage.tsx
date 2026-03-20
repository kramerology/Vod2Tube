import { useCallback, useEffect, useRef, useState } from 'react';
import {
  vodsApi,
  type PipelineJob,
  isActive, isPending, isCompleted, isFailed,
  formatDuration, stageLabel,
} from '../api/client';

// ── Stage colour / icon map ───────────────────────────────────────────────────

function stageStyle(job: PipelineJob): { color: string; bg: string; border: string } {
  if (job.failed)                     return { color: 'text-error',    bg: 'bg-error-container/15',    border: 'border-error/20' };
  if (job.stage === 'Uploaded')       return { color: 'text-tertiary', bg: 'bg-tertiary-container/15', border: 'border-tertiary/20' };
  if (job.stage === 'Cancelled')      return { color: 'text-on-surface-variant', bg: 'bg-surface-variant/40', border: 'border-outline-variant/20' };
  if (isActive(job))                  return { color: 'text-secondary', bg: 'bg-secondary-container/15', border: 'border-secondary/20' };
  if (isPending(job))                 return { color: 'text-primary',   bg: 'bg-primary-container/15',  border: 'border-primary/20' };
  return { color: 'text-on-surface-variant', bg: 'bg-surface-variant/40', border: 'border-outline-variant/20' };
}

function stageIcon(job: PipelineJob): string {
  if (job.failed)                        return 'error';
  if (job.stage === 'Uploaded')          return 'check_circle';
  if (job.stage === 'Cancelled')         return 'cancel';
  if (job.stage.includes('Download'))    return 'download';
  if (job.stage.includes('Chat'))        return 'chat';
  if (job.stage.includes('Render') || job.stage.includes('Combin')) return 'movie';
  if (job.stage.includes('Upload'))      return 'cloud_upload';
  return 'hourglass_empty';
}

// ── VOD Job Card ──────────────────────────────────────────────────────────────

function JobCard({
  job,
  onPause, onResume, onCancel, onRetry,
}: {
  job: PipelineJob;
  onPause: () => void;
  onResume: () => void;
  onCancel: () => void;
  onRetry: () => void;
}) {
  const s = stageStyle(job);
  const done = isCompleted(job);
  const date = new Date(job.createdAtUTC).toLocaleDateString('en-US', {
    month: 'short', day: 'numeric', year: 'numeric',
  });

  return (
    <div
      className={`bg-surface-container-high rounded-xl overflow-hidden group transition-all duration-300 ${
        job.failed ? 'hover:ring-1 hover:ring-error/30' : 'hover:ring-1 hover:ring-primary/30'
      } ${done && !job.failed ? 'opacity-70 hover:opacity-100' : ''}`}
    >
      {/* Thumbnail banner */}
      <div className={`h-36 relative overflow-hidden ${done ? 'grayscale-[0.5]' : ''}`}>
        <div
          className="absolute inset-0"
          style={{
            background: job.failed
              ? 'linear-gradient(135deg, #93000a 0%, #2d3449 100%)'
              : isActive(job)
                ? 'linear-gradient(135deg, #ee9800 0%, #0b1326 100%)'
                : isPending(job)
                  ? 'linear-gradient(135deg, #4d8eff 0%, #0b1326 100%)'
                  : job.stage === 'Uploaded'
                    ? 'linear-gradient(135deg, #00a572 0%, #0b1326 100%)'
                    : 'linear-gradient(135deg, #424754 0%, #0b1326 100%)',
          }}
        />
        <div className="absolute inset-0 bg-gradient-to-t from-surface-container-high via-transparent to-transparent" />

        {/* Stage badge */}
        <div className="absolute top-3 left-3">
          <span className={`px-2.5 py-1 rounded text-[10px] font-bold uppercase tracking-wider border ${s.color} ${s.bg} ${s.border}`}>
            {stageLabel(job)}
          </span>
        </div>

        {/* Paused badge */}
        {job.paused && (
          <div className="absolute top-3 right-3">
            <span className="flex items-center gap-1 px-2.5 py-1 rounded text-[10px] font-bold uppercase tracking-wider bg-secondary-container/15 text-secondary border border-secondary/20">
              <span className="material-symbols-outlined text-xs">pause</span>
              Paused
            </span>
          </div>
        )}

        {/* Duration */}
        <div className="absolute bottom-3 right-3 text-[10px] font-mono text-on-surface/60">
          {formatDuration(job.duration)}
        </div>

        {/* Stage icon */}
        <div className="absolute bottom-3 left-3">
          <span className={`material-symbols-outlined text-xl ${s.color} opacity-80`}>
            {stageIcon(job)}
          </span>
        </div>
      </div>

      {/* Body */}
      <div className="p-5">
        <div className="mb-3">
          <h3 className="font-bold text-base leading-tight mb-1 line-clamp-2" title={job.title}>
            {job.title || job.vodId}
          </h3>
          <p className="text-xs text-on-surface-variant">
            <span className="text-primary">{job.channelName}</span>
            {job.channelName && ' · '}
            {date}
          </p>
        </div>

        {/* Description / progress hint */}
        {job.description && (
          <p className="text-xs text-on-surface-variant mb-3 line-clamp-1">{job.description}</p>
        )}

        {/* Fail reason */}
        {job.failed && job.failReason && (
          <div className="mb-3 p-2 bg-error-container/20 border border-error/20 rounded-lg text-xs text-error line-clamp-2">
            {job.failReason}
          </div>
        )}

        {/* Active progress bar */}
        {isActive(job) && (
          <div className="mb-4">
            <div className="h-1.5 w-full bg-surface-variant rounded-full overflow-hidden">
              <div className="h-full bg-gradient-to-r from-secondary to-on-secondary-container rounded-full animate-pulse w-full" />
            </div>
          </div>
        )}

        {/* Actions */}
        <div className="flex items-center gap-2 mt-auto">
          {/* Twitch link */}
          {job.vodUrl && (
            <a
              href={job.vodUrl}
              target="_blank"
              rel="noreferrer"
              title="View on Twitch"
              className="p-1.5 text-on-surface-variant hover:text-primary transition-colors"
            >
              <span className="material-symbols-outlined text-lg">tv</span>
            </a>
          )}
          {/* YouTube link */}
          {job.youtubeVideoId && (
            <a
              href={`https://www.youtube.com/watch?v=${job.youtubeVideoId}`}
              target="_blank"
              rel="noreferrer"
              title="Watch on YouTube"
              className="p-1.5 text-on-surface-variant hover:text-error transition-colors"
            >
              <span className="material-symbols-outlined text-lg">play_circle</span>
            </a>
          )}

          <div className="ml-auto flex items-center gap-1">
            {/* Completed / cancelled — retry/requeue */}
            {done && (
              <button
                onClick={onRetry}
                title="Re-queue"
                className="p-1.5 rounded-lg hover:bg-primary/10 text-primary transition-colors"
              >
                <span className="material-symbols-outlined text-lg">replay</span>
              </button>
            )}

            {/* Active / pending controls */}
            {!done && (
              <>
                {job.paused ? (
                  <button
                    onClick={onResume}
                    title="Resume"
                    className="p-1.5 rounded-lg hover:bg-tertiary/10 text-tertiary transition-colors"
                  >
                    <span className="material-symbols-outlined text-lg">play_arrow</span>
                  </button>
                ) : !job.failed && (
                  <button
                    onClick={onPause}
                    title="Pause"
                    className="p-1.5 rounded-lg hover:bg-secondary/10 text-secondary transition-colors"
                  >
                    <span className="material-symbols-outlined text-lg">pause</span>
                  </button>
                )}

                {job.failed && (
                  <button
                    onClick={onRetry}
                    title="Retry"
                    className="p-1.5 rounded-lg hover:bg-primary/10 text-primary transition-colors"
                  >
                    <span className="material-symbols-outlined text-lg">refresh</span>
                  </button>
                )}

                <button
                  onClick={onCancel}
                  title="Cancel"
                  className="p-1.5 rounded-lg hover:bg-error/10 text-error transition-colors"
                >
                  <span className="material-symbols-outlined text-lg">cancel</span>
                </button>
              </>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

// ── Filter chip ───────────────────────────────────────────────────────────────

type Filter = 'all' | 'active' | 'pending' | 'paused' | 'failed' | 'completed';

function FilterChip({ label, active, count, onClick }: {
  label: string; active: boolean; count: number; onClick: () => void;
}) {
  return (
    <button
      onClick={onClick}
      className={`px-4 py-1.5 rounded-full text-xs font-bold transition-all ${
        active
          ? 'bg-primary text-on-primary shadow-lg shadow-primary/10'
          : 'bg-surface-container-lowest text-on-surface-variant hover:bg-surface-bright'
      }`}
    >
      {label} <span className="ml-1 opacity-70">({count})</span>
    </button>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────

export default function VodsPage() {
  const [jobs, setJobs] = useState<PipelineJob[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<Filter>('all');
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const load = useCallback(async () => {
    try {
      setError(null);
      const data = await vodsApi.getAll();
      setJobs(data);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    load();
    timerRef.current = setInterval(load, 3000);
    return () => { if (timerRef.current) clearInterval(timerRef.current); };
  }, [load]);

  async function act(fn: () => Promise<void>) {
    await fn();
    await load();
  }

  const filtered = jobs.filter(j => {
    if (filter === 'all')       return true;
    if (filter === 'active')    return isActive(j);
    if (filter === 'pending')   return isPending(j);
    if (filter === 'paused')    return j.paused;
    if (filter === 'failed')    return isFailed(j);
    if (filter === 'completed') return isCompleted(j);
    return true;
  });

  const counts = {
    all:       jobs.length,
    active:    jobs.filter(isActive).length,
    pending:   jobs.filter(isPending).length,
    paused:    jobs.filter(j => j.paused).length,
    failed:    jobs.filter(isFailed).length,
    completed: jobs.filter(isCompleted).length,
  };

  return (
    <>
      {/* Header */}
      <div className="flex flex-col md:flex-row md:items-end justify-between gap-6 mb-8">
        <div>
          <h1 className="text-4xl font-bold tracking-tighter">Processing Queue</h1>
          <p className="text-on-surface-variant mt-2 max-w-xl">
            Real-time telemetry for automated Twitch VOD archival. Auto-refreshes every 3 s.
          </p>
        </div>
        <div className="flex items-center gap-2 text-xs text-on-surface-variant">
          <span className="w-2 h-2 rounded-full bg-tertiary animate-pulse" />
          Live
        </div>
      </div>

      {/* Filter chips */}
      <div className="flex flex-wrap items-center gap-2 mb-8">
        <span className="text-[10px] uppercase tracking-widest text-on-surface-variant mr-1">Filters:</span>
        {(['all', 'active', 'pending', 'paused', 'failed', 'completed'] as Filter[]).map(f => (
          <FilterChip
            key={f}
            label={f.charAt(0).toUpperCase() + f.slice(1)}
            active={filter === f}
            count={counts[f]}
            onClick={() => setFilter(f)}
          />
        ))}
      </div>

      {/* Error */}
      {error && (
        <div className="mb-6 p-4 bg-error-container/20 border border-error/30 rounded-xl text-error flex items-center gap-3">
          <span className="material-symbols-outlined">error</span>
          <span>{error}</span>
          <button onClick={load} className="ml-auto text-xs font-bold underline">Retry</button>
        </div>
      )}

      {/* Loading */}
      {loading && (
        <div className="flex items-center justify-center py-20">
          <div className="w-8 h-8 border-2 border-primary/30 border-t-primary rounded-full animate-spin" />
        </div>
      )}

      {/* Grid */}
      {!loading && filtered.length > 0 && (
        <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-5">
          {filtered.map(job => (
            <JobCard
              key={job.vodId}
              job={job}
              onPause={() => act(() => vodsApi.pause(job.vodId))}
              onResume={() => act(() => vodsApi.resume(job.vodId))}
              onCancel={() => act(() => vodsApi.cancel(job.vodId))}
              onRetry={() => act(() => vodsApi.retry(job.vodId))}
            />
          ))}
        </div>
      )}

      {/* Empty state */}
      {!loading && filtered.length === 0 && (
        <div className="flex flex-col items-center justify-center py-24 gap-4">
          <span className="material-symbols-outlined text-5xl text-on-surface-variant">video_library</span>
          <p className="text-on-surface-variant text-lg font-bold">No VODs match this filter</p>
          <p className="text-on-surface-variant text-sm">
            {filter === 'all' ? 'No jobs in the queue yet.' : 'Try selecting a different filter above.'}
          </p>
        </div>
      )}
    </>
  );
}
